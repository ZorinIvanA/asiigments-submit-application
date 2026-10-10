using System.Net;
using System.Text;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using LabsApp.Tests.Hosting;
using LabsApp.Tests.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.Tests.Students;

// ============================================================================
// Эндпойнт-тесты контроллера Students (C-009, IF-011; FR-020 + роль-матрица
// FR-022, представитель группы students*). Сессии — DI-минт access-JWT через
// ITokenService хоста (ADR-015): POST /auth/login не используется. Данные —
// демо-сид (группы ИК-221×25 / ИК-222×5 / ИК-223×0, студенты student01..32,
// из них student31/student32 без группы); дополнительные студенты сеются
// DI-сидом через репозитории своего сценария (TestEntities).
// ============================================================================

/// <summary>Фикстура хоста с гарантированным демо-сидом (3 группы, 32 студента).</summary>
public sealed class StudentsApiFixture : IDisposable
{
    public TestWebAppFactory Factory { get; } = new(
        null,
        new Dictionary<string, string?> { [SeedOptions.DemoDataVariable] = "true" });

    public void Dispose() => Factory.Dispose();
}

/// <summary>Фикстура хоста без демо-набора: только сид-преподаватель — полный
/// контроль над составом студентов для сценариев сортировки.</summary>
public sealed class NoDemoStudentsApiFixture : IDisposable
{
    public TestWebAppFactory Factory { get; } = new(
        null,
        new Dictionary<string, string?> { [SeedOptions.DemoDataVariable] = "false" });

    public void Dispose() => Factory.Dispose();
}

/// <summary>
/// Общий харнес students-эндпойнтов: сессии (teacher/student/анонимно), DI-сид
/// студентов, доступ к репозиториям хоста, чтение JSON-тел и конверта ошибок IF-001.
/// </summary>
internal static class StudentsEndpointHarness
{
    public const string StudentsEndpoint = "/api/v1/students";

    public static HttpClient CreateTeacherClient(WebApplicationFactory<Program> factory)
    {
        var teacher = factory.Services.GetRequiredService<IUserRepository>().GetByLogin("teacher");
        Assert.NotNull(teacher);
        return CreateSessionClient(factory, teacher.Id, UserRoles.Teacher);
    }

    public static HttpClient CreateStudentClient(WebApplicationFactory<Program> factory)
    {
        var student = factory.Services.GetRequiredService<IUserRepository>().GetByLogin("student01");
        Assert.NotNull(student);
        return CreateSessionClient(factory, student.Id, UserRoles.Student);
    }

    public static HttpClient CreateAnonymousClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    public static IGroupRepository Groups(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IGroupRepository>();

    public static IUserRepository Users(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IUserRepository>();

    public static Group GroupByName(WebApplicationFactory<Program> factory, string name) =>
        factory.Services.GetRequiredService<IGroupRepository>().GetByName(name)
        ?? throw new InvalidOperationException($"Группы {name} нет в хранилище тестового хоста.");

    public static User StudentByLogin(WebApplicationFactory<Program> factory, string login) =>
        factory.Services.GetRequiredService<IUserRepository>().GetByLogin(login)
        ?? throw new InvalidOperationException($"Студента {login} нет в хранилище тестового хоста.");

    public static User SeedStudent(
        WebApplicationFactory<Program> factory,
        string login,
        string fullName,
        Guid? groupId)
    {
        var repository = Users(factory);
        repository.Add(TestEntities.User(login, fullName: fullName, groupId: groupId));
        return repository.GetByLogin(login)
            ?? throw new InvalidOperationException($"Студент {login} не сохранился при DI-сиде.");
    }

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    /// <summary>Логины items страницы в порядке выдачи (сортировка/нарезка).</summary>
    public static string[] Logins(JsonElement root) =>
        [.. root.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("login").GetString() ?? string.Empty)];

    /// <summary>Верхнеуровневый message конверта ошибок (IF-001).</summary>
    public static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Object, body.RootElement.ValueKind);
        return body.RootElement.GetProperty("message").GetString() ?? string.Empty;
    }

    /// <summary>Свойство errors 400 (клон: элемент живёт после dispose документа).</summary>
    public static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        using var body = await ReadJsonAsync(response);
        Assert.True(
            body.RootElement.TryGetProperty("errors", out var errors),
            "Ожидалось свойство errors в теле 400.");
        return errors.Clone();
    }

    public static string[] ErrorOf(JsonElement errors, string field) =>
        [.. errors.GetProperty(field).EnumerateArray().Select(item => item.GetString() ?? string.Empty)];

    private static HttpClient CreateSessionClient(WebApplicationFactory<Program> factory, Guid userId, string role)
    {
        var token = factory.Services
            .GetRequiredService<ITokenService>()
            .IssueAccessToken(userId, role);

        var client = CreateAnonymousClient(factory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={token}");
        return client;
    }
}

/// <summary>
/// FR-020, многословный поиск: все токены — ci-подстроки ОДНОГО И ТОГО ЖЕ поля
/// (fullName|login|email), порядок токенов не значим; демо-сид: 32 студента
/// «Иванов Иван Иванович NN» / studentNN / studentNN@example.com + преподаватель.
/// </summary>
public sealed class StudentsSearchEndpointTests(StudentsApiFixture fixture) : IClassFixture<StudentsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Search_MultiWordBothTokensInFullName_ReturnsOnlyStudent01()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        // «иван» есть в FullName всех сид-студентов, «01» — только в FullName
        // student01 (суффикс); в login/email других студентов «иван» отсутствует.
        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?search={Uri.EscapeDataString("иван 01")}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "student01" }, StudentsEndpointHarness.Logins(body.RootElement));
    }

    [Fact]
    public async Task Search_TokensInDifferentFields_DoNotCombine()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        // «иванов» — только в FullName, email — другое поле: сложения токенов
        // из разных полей нет (IF-011).
        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?search={Uri.EscapeDataString("иванов student01@example.com")}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Search_TokenOrderIrrelevant_SameResult()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?search={Uri.EscapeDataString("01 иван")}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "student01" }, StudentsEndpointHarness.Logins(body.RootElement));
    }

    [Theory]
    [InlineData("student05")]
    [InlineData("STUDENT05")]
    [InlineData("student05@example.com")]
    public async Task Search_SingleToken_MatchesLoginOrEmail_CaseInsensitive(string query)
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?search={Uri.EscapeDataString(query)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "student05" }, StudentsEndpointHarness.Logins(body.RootElement));
    }

    [Fact]
    public async Task Search_TeacherFullName_MatchesNoStudent()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        // Перечень содержит ТОЛЬКО role=student: «Сидоров …» преподавателя не находится.
        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?search={Uri.EscapeDataString("сидоров")}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_EmptyOrWhitespace_NoFilter(string query)
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?search={Uri.EscapeDataString(query)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(32, body.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Search_LongerThan200AfterTrim_Returns400WithSearchFieldError()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?search={Uri.EscapeDataString(new string('х', 201))}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await StudentsEndpointHarness.MessageAsync(response));
        var errors = await StudentsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            new[] { "Поиск — не более 200 символов" },
            StudentsEndpointHarness.ErrorOf(errors, "search"));
    }

    [Fact]
    public async Task Search_Exactly200Chars_IsValid()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        // Граница включительно: ровно 200 символов после трима — валидный запрос.
        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?search={Uri.EscapeDataString(new string('х', 200))}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
    }
}

/// <summary>
/// FR-020, фильтр группы перечня: отсутствует — без фильтра, «none» — только
/// без группы, uuid — точное равенство; НЕИЗВЕСТНЫЙ uuid — пустая выборка, НЕ 404.
/// </summary>
public sealed class StudentsGroupFilterEndpointTests(StudentsApiFixture fixture) : IClassFixture<StudentsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task GroupFilterNone_ReturnsOnlyUngroupedStudents()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?groupId=none");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(2, body.RootElement.GetProperty("total").GetInt32());

        // В сиде без группы student31 и student32; сортировка fullName↑ даёт 31 → 32.
        Assert.Equal(new[] { "student31", "student32" }, StudentsEndpointHarness.Logins(body.RootElement));
        foreach (var item in body.RootElement.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Null, item.GetProperty("groupId").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("groupName").ValueKind);
        }
    }

    [Fact]
    public async Task GroupFilterGroupUuid_ReturnsOnlyThatGroup()
    {
        var ik221 = StudentsEndpointHarness.GroupByName(_factory, "ИК-221");
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?groupId={ik221.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(25, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("items").GetArrayLength());
        foreach (var item in body.RootElement.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(ik221.Id.ToString(), item.GetProperty("groupId").GetString());
            Assert.Equal("ИК-221", item.GetProperty("groupName").GetString());
        }
    }

    [Fact]
    public async Task GroupFilterEmptyGroupUuid_ReturnsEmptyList()
    {
        var ik223 = StudentsEndpointHarness.GroupByName(_factory, "ИК-223");
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?groupId={ik223.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GroupFilterUnknownUuid_ReturnsEmptyListNot404()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?groupId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task GroupFilterNotUuidAndNotNone_ReturnsEmptyList()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?groupId={Uri.EscapeDataString("не-uuid")}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GroupFilterCombinedWithSearch_AppliesBothFilters()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?groupId=none&search={Uri.EscapeDataString("иван 31")}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "student31" }, StudentsEndpointHarness.Logins(body.RootElement));
    }
}

/// <summary>
/// FR-020, пагинация: pageSize=10, нормализация page (некорректное значение → 1
/// без эха), страница правее последней — пустые items при корректном total;
/// проекция StudentDto (id, fullName, login, email, groupId|null, groupName|null).
/// </summary>
public sealed class StudentsListEndpointTests(StudentsApiFixture fixture) : IClassFixture<StudentsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task List_DefaultPage_FirstTenStudentsWithTotals()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(StudentsEndpointHarness.StudentsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(32, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("pageSize").GetInt32());

        // Сортировка fullName↑: «…Иванович 01»..«…Иванович 10» — student01..student10.
        Assert.Equal(
            new[] { "student01", "student02", "student03", "student04", "student05",
                    "student06", "student07", "student08", "student09", "student10" },
            StudentsEndpointHarness.Logins(body.RootElement));
    }

    [Fact]
    public async Task List_LastPage_TwoRemainingStudents()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync($"{StudentsEndpointHarness.StudentsEndpoint}?page=4");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(32, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(4, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(new[] { "student31", "student32" }, StudentsEndpointHarness.Logins(body.RootElement));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("2.5")]
    public async Task List_InvalidPage_NormalizedToOne(string rawPage)
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?page={Uri.EscapeDataString(rawPage)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        // Эхо некорректного значения запрещено — только нормализованный номер.
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(32, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_PageBeyondLast_EmptyItemsWithCorrectTotal()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync($"{StudentsEndpointHarness.StudentsEndpoint}?page=99");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(99, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(32, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData(214748365)] // последний page, чьё (page-1)*10 ещё помещается в int
    [InlineData(214748366)] // первый page с int-переполнением смещения (CR-001)
    [InlineData(int.MaxValue)]
    public async Task List_HugePage_IntOffsetOverflowStillEmptyItems(int rawPage)
    {
        // CR-001: для page ≥ 214 748 366 смещение (page-1)*PageSize, посчитанное в
        // int, заворачивалось в отрицательное — Skip отдавал ПЕРВУЮ страницу вместо
        // пустой. Страница правее последней обязана быть пустой при корректном
        // total (IF-011), ответ эхом отдаёт нормализованный номер страницы.
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?page={rawPage}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(rawPage, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(32, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_DtoProjection_GroupedStudent_AllFieldsMapped()
    {
        var ik221 = StudentsEndpointHarness.GroupByName(_factory, "ИК-221");
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?search=student01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        var item = body.RootElement.GetProperty("items")[0];
        Assert.Equal("Иванов Иван Иванович 01", item.GetProperty("fullName").GetString());
        Assert.Equal("student01", item.GetProperty("login").GetString());
        Assert.Equal("student01@example.com", item.GetProperty("email").GetString());
        Assert.Equal(ik221.Id.ToString(), item.GetProperty("groupId").GetString());
        Assert.Equal("ИК-221", item.GetProperty("groupName").GetString());
        Assert.True(Guid.TryParse(item.GetProperty("id").GetString(), out _));
    }

    [Fact]
    public async Task List_DtoProjection_UngroupedStudent_GroupFieldsNull()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?search=student32");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        var item = body.RootElement.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, item.GetProperty("groupId").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("groupName").ValueKind);
    }
}

/// <summary>
/// FR-020, сортировка ДО нарезки по правилам русской локали без учёта регистра
/// (AR-005): полный набор из 11 студентов, где ordinal-порядок противоречит
/// ru-ci — «Б…» (U+0411) раньше «а…» (U+0430) при ordinal, поэтому различимо,
/// сортируется ли ВЕСЬ набор до нарезки первой страницы.
/// </summary>
public sealed class StudentsSortRuLocaleEndpointTests(NoDemoStudentsApiFixture fixture) : IClassFixture<NoDemoStudentsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Sort_RuLocaleCi_AppliedToFullSetBeforeSlicing()
    {
        for (var n = 1; n <= 10; n++)
        {
            StudentsEndpointHarness.SeedStudent(
                _factory, $"sa-{n:00}", $"а{n:00}", groupId: null);
        }

        // Ordinal поставил бы «Б01» (U+0411) первым; ru-ci ставит его ПОСЛЕ
        // всех «а01»..«а10» — на второй странице (pageSize=10, total=11).
        StudentsEndpointHarness.SeedStudent(_factory, "sb-01", "Б01", groupId: null);
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var firstPage = await client.GetAsync(StudentsEndpointHarness.StudentsEndpoint);
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        using var firstBody = await StudentsEndpointHarness.ReadJsonAsync(firstPage);
        Assert.Equal(11, firstBody.RootElement.GetProperty("total").GetInt32());

        var firstPageLogins = StudentsEndpointHarness.Logins(firstBody.RootElement);
        Assert.Equal(10, firstPageLogins.Length);
        Assert.DoesNotContain("sb-01", firstPageLogins);
        Assert.Equal(
            new[] { "sa-01", "sa-02", "sa-03", "sa-04", "sa-05", "sa-06", "sa-07", "sa-08", "sa-09", "sa-10" },
            firstPageLogins);

        using var secondPage = await client.GetAsync($"{StudentsEndpointHarness.StudentsEndpoint}?page=2");
        Assert.Equal(HttpStatusCode.OK, secondPage.StatusCode);
        using var secondBody = await StudentsEndpointHarness.ReadJsonAsync(secondPage);
        Assert.Equal(new[] { "sb-01" }, StudentsEndpointHarness.Logins(secondBody.RootElement));
    }
}

/// <summary>
/// FR-020, двухколоночная сортировка: при равном fullName — login↑ по правилам
/// русской локали без учёта регистра (ordinal дал бы обратный порядок).
/// </summary>
public sealed class StudentsSortTiebreakEndpointTests(NoDemoStudentsApiFixture fixture) : IClassFixture<NoDemoStudentsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Sort_EqualFullName_LoginTiebreakRuCi()
    {
        // Сид в «неудобном» порядке: хранилище порядок не определяёт (IF-015),
        // но вставка первым «Бета-логина» исключает случайное совпадение с сортировкой.
        StudentsEndpointHarness.SeedStudent(_factory, "g-borisov", "Борисов Борис", groupId: null);
        StudentsEndpointHarness.SeedStudent(_factory, "Бета-логин", "антонов Арат", groupId: null);
        StudentsEndpointHarness.SeedStudent(_factory, "альфа-логин", "антонов Арат", groupId: null);
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(StudentsEndpointHarness.StudentsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await StudentsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(3, body.RootElement.GetProperty("total").GetInt32());
        var pairs = body.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => (FullName: item.GetProperty("fullName").GetString(), Login: item.GetProperty("login").GetString()))
            .ToArray();
        Assert.Equal(
            new (string? FullName, string? Login)[]
            {
                (FullName: "антонов Арат", Login: "альфа-логин"),
                (FullName: "антонов Арат", Login: "Бета-логин"),
                (FullName: "Борисов Борис", Login: "g-borisov"),
            },
            pairs);
    }
}

/// <summary>
/// FR-020, PUT /students/{id}/group: три состояния поля groupId (ADR-014) НЕ
/// схлопываются — String: включение/перевод, JsonNull: снятие (204), Invalid
/// (отсутствует/нестроковое/битый JSON): 404 «Группа не найдена»; id не найден
/// или роль ≠ student → 404 «Студент не найден».
/// </summary>
public sealed class StudentsSetGroupEndpointTests(StudentsApiFixture fixture) : IClassFixture<StudentsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task SetGroup_AssignThenUnassign_ThreeStatesSequence()
    {
        var ik223 = StudentsEndpointHarness.GroupByName(_factory, "ИК-223");
        var student31 = StudentsEndpointHarness.StudentByLogin(_factory, "student31");
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        // (1) Kind=String — включение в группу: 204 и перевод.
        using var assign = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{student31.Id}/group",
            StudentsEndpointHarness.Json($$"""{"groupId":"{{ik223.Id}}"}"""));
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        Assert.Equal(string.Empty, await assign.Content.ReadAsStringAsync());

        using var afterAssign = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?search=student31");
        using var afterAssignBody = await StudentsEndpointHarness.ReadJsonAsync(afterAssign);
        Assert.Equal(1, afterAssignBody.RootElement.GetProperty("total").GetInt32());
        var assigned = afterAssignBody.RootElement.GetProperty("items")[0];
        Assert.Equal(ik223.Id.ToString(), assigned.GetProperty("groupId").GetString());
        Assert.Equal("ИК-223", assigned.GetProperty("groupName").GetString());

        // (2) Kind=JsonNull — исключение из группы: 204 и groupId=null.
        using var unassign = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{student31.Id}/group",
            StudentsEndpointHarness.Json("""{"groupId":null}"""));
        Assert.Equal(HttpStatusCode.NoContent, unassign.StatusCode);

        using var afterUnassign = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?search=student31");
        using var afterUnassignBody = await StudentsEndpointHarness.ReadJsonAsync(afterUnassign);
        Assert.Equal(1, afterUnassignBody.RootElement.GetProperty("total").GetInt32());
        var unassigned = afterUnassignBody.RootElement.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, unassigned.GetProperty("groupId").ValueKind);
        Assert.Equal(JsonValueKind.Null, unassigned.GetProperty("groupName").ValueKind);

        // (3) Kind=Invalid (нестроковое значение) — 404 «Группа не найдена».
        using var nonString = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{student31.Id}/group",
            StudentsEndpointHarness.Json("""{"groupId":123}"""));
        Assert.Equal(HttpStatusCode.NotFound, nonString.StatusCode);
        Assert.Equal("Группа не найдена", await StudentsEndpointHarness.MessageAsync(nonString));

        // (4) Kind=Invalid (поле отсутствует) — тот же 404 «Группа не найдена».
        using var missing = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{student31.Id}/group",
            StudentsEndpointHarness.Json("""{}"""));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Группа не найдена", await StudentsEndpointHarness.MessageAsync(missing));

        // Состояние записи ветками Invalid не меняется (группы по-прежнему нет).
        Assert.Null(StudentsEndpointHarness.StudentByLogin(_factory, "student31").GroupId);
    }

    [Fact]
    public async Task SetGroup_TransferBetweenGroups_CountsFollow()
    {
        var ik221 = StudentsEndpointHarness.GroupByName(_factory, "ИК-221");
        var ik222 = StudentsEndpointHarness.GroupByName(_factory, "ИК-222");
        var student30 = StudentsEndpointHarness.StudentByLogin(_factory, "student30");
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var transfer = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{student30.Id}/group",
            StudentsEndpointHarness.Json($$"""{"groupId":"{{ik221.Id}}"}"""));
        Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);

        using var target = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?groupId={ik221.Id}&search=student30");
        using var targetBody = await StudentsEndpointHarness.ReadJsonAsync(target);
        Assert.Equal(1, targetBody.RootElement.GetProperty("total").GetInt32());
        var transferred = targetBody.RootElement.GetProperty("items")[0];
        Assert.Equal(ik221.Id.ToString(), transferred.GetProperty("groupId").GetString());
        Assert.Equal("ИК-221", transferred.GetProperty("groupName").GetString());

        using var source = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?groupId={ik222.Id}");
        using var sourceBody = await StudentsEndpointHarness.ReadJsonAsync(source);
        Assert.Equal(4, sourceBody.RootElement.GetProperty("total").GetInt32());
        Assert.DoesNotContain("student30", StudentsEndpointHarness.Logins(sourceBody.RootElement));
    }

    [Fact]
    public async Task SetGroup_UnknownGroupUuid_Returns404GroupNotFound_KeepsStudent()
    {
        var student32 = StudentsEndpointHarness.StudentByLogin(_factory, "student32");
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{student32.Id}/group",
            StudentsEndpointHarness.Json($$"""{"groupId":"{{Guid.NewGuid()}}"}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Группа не найдена", await StudentsEndpointHarness.MessageAsync(response));
        Assert.Null(StudentsEndpointHarness.StudentByLogin(_factory, "student32").GroupId);
    }

    [Fact]
    public async Task SetGroup_NonUuidGroupString_Returns404GroupNotFound()
    {
        var student32 = StudentsEndpointHarness.StudentByLogin(_factory, "student32");
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{student32.Id}/group",
            StudentsEndpointHarness.Json("""{"groupId":"abc"}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Группа не найдена", await StudentsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task SetGroup_BrokenJsonBody_Returns404GroupNotFound()
    {
        var student32 = StudentsEndpointHarness.StudentByLogin(_factory, "student32");
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        // 400-ветки на groupId НЕТ: битый JSON — состояние Invalid → 404 (IF-011).
        using var response = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{student32.Id}/group",
            StudentsEndpointHarness.Json("""{"groupId": broken"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Группа не найдена", await StudentsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task SetGroup_UnknownStudentId_Returns404StudentNotFound()
    {
        var ik223 = StudentsEndpointHarness.GroupByName(_factory, "ИК-223");
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{Guid.NewGuid()}/group",
            StudentsEndpointHarness.Json($$"""{"groupId":"{{ik223.Id}}"}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Студент не найден", await StudentsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task SetGroup_TeacherId_Returns404StudentNotFound()
    {
        var teacher = StudentsEndpointHarness.Users(_factory).GetByLogin("teacher")
            ?? throw new InvalidOperationException("Сид-преподаватель не найден.");
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        // Роль ≠ student приравнена к отсутствию: даже JsonNull (снятие) даёт 404.
        using var response = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{teacher.Id}/group",
            StudentsEndpointHarness.Json("""{"groupId":null}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Студент не найден", await StudentsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task SetGroup_NonUuidRouteId_Returns404StudentNotFound()
    {
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/not-a-guid/group",
            StudentsEndpointHarness.Json("""{"groupId":null}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Студент не найден", await StudentsEndpointHarness.MessageAsync(response));
    }
}

/// <summary>
/// FR-022 (представитель группы students*): анонимно — 401, student — 403,
/// причём роль проверяется раньше валидации параметров (400), разбора тела и
/// поиска записи (404); teacher — статус по бизнес-правилам.
/// </summary>
public sealed class StudentsRolesEndpointTests(StudentsApiFixture fixture) : IClassFixture<StudentsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task List_Anonymous_Returns401Envelope()
    {
        using var client = StudentsEndpointHarness.CreateAnonymousClient(_factory);

        using var response = await client.GetAsync(StudentsEndpointHarness.StudentsEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await StudentsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task List_Student_Returns403Envelope()
    {
        using var client = StudentsEndpointHarness.CreateStudentClient(_factory);

        using var response = await client.GetAsync(StudentsEndpointHarness.StudentsEndpoint);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await StudentsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task List_StudentTooLongSearch_403Before400()
    {
        using var client = StudentsEndpointHarness.CreateStudentClient(_factory);

        // Роль проверяется раньше валидации: слишком длинный search не даёт 400.
        using var response = await client.GetAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}?search={Uri.EscapeDataString(new string('х', 201))}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await StudentsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task SetGroup_Anonymous_Returns401Envelope()
    {
        using var client = StudentsEndpointHarness.CreateAnonymousClient(_factory);

        using var response = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{Guid.NewGuid()}/group",
            StudentsEndpointHarness.Json("""{"groupId":null}"""));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await StudentsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task SetGroup_StudentUnknownIdBrokenJson_403Before400And404()
    {
        using var client = StudentsEndpointHarness.CreateStudentClient(_factory);

        // Роль раньше разбора тела (400-ветки нет) и поиска записи (404).
        using var response = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{Guid.NewGuid()}/group",
            StudentsEndpointHarness.Json("""{"groupId": broken"""));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await StudentsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task TeacherOwnRole_BusinessStatuses()
    {
        var ik223 = StudentsEndpointHarness.GroupByName(_factory, "ИК-223");
        var student31 = StudentsEndpointHarness.StudentByLogin(_factory, "student31");
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        using var list = await client.GetAsync(StudentsEndpointHarness.StudentsEndpoint);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        using var assign = await client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{student31.Id}/group",
            StudentsEndpointHarness.Json($$"""{"groupId":"{{ik223.Id}}"}"""));
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
    }
}
