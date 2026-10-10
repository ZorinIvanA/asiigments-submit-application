using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using LabsApp.Tests.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.Tests.Submissions;

// ============================================================================
// Эндпойнт-тесты контроллера Submissions (C-010, IF-012; FR-021 + роль-матрица
// FR-022, представитель группы submissions). Сессии — DI-минт access-JWT через
// ITokenService хоста (ADR-015): POST /auth/login не используется. Данные —
// демо-сид (ИК-221×25 с student01..25, student31/32 без группы, работы
// семестра 1 №1–20 и семестра 2 №1–3, сид-сдачи student01×3 и student02×1);
// признак демо-набора закреплён ЯВНО в настройках хоста.
// Строгий semester (ISS-011/AR-002): 400 с errors.semester, не 200 с пустыми
// массивами; дата-поля PUT — 400 ДО разрешения сущностей (400 раньше 404).
// ============================================================================

/// <summary>Фикстура хоста с гарантированным демо-сидом и Labs__MaxSemester=10.</summary>
public sealed class SubmissionsApiFixture : IDisposable
{
    public TestWebAppFactory Factory { get; } = new(
        null,
        new Dictionary<string, string?>
        {
            [SeedOptions.DemoDataVariable] = "true",
            [LabsOptions.MaxSemesterVariable] = "10",
        });

    public void Dispose() => Factory.Dispose();
}

/// <summary>
/// Общий харнес submissions-эндпойнтов: сессии (teacher/student/анонимно),
/// доступ к репозиториям хоста, чтение JSON-тел и конверта ошибок IF-001.
/// </summary>
internal static class SubmissionsEndpointHarness
{
    public const string GridEndpoint = "/api/v1/submissions";
    public const string MyEndpoint = "/api/v1/me/submissions";

    /// <summary>Размер страницы студентов ведомости (FR-021: по 5).</summary>
    public const int PageSize = 5;

    public static HttpClient CreateTeacherClient(TestWebAppFactory factory) =>
        CreateClient(factory, UserByLogin(factory, "teacher"));

    public static HttpClient CreateStudentClient(TestWebAppFactory factory, string login) =>
        CreateClient(factory, UserByLogin(factory, login));

    public static HttpClient CreateAnonymousClient(TestWebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    public static User UserByLogin(TestWebAppFactory factory, string login) =>
        factory.Services.GetRequiredService<IUserRepository>().GetByLogin(login)
        ?? throw new InvalidOperationException($"Пользователя {login} нет в хранилище тестового хоста.");

    public static Group GroupByName(TestWebAppFactory factory, string name) =>
        factory.Services.GetRequiredService<IGroupRepository>().GetByName(name)
        ?? throw new InvalidOperationException($"Группы {name} нет в хранилище тестового хоста.");

    public static Lab LabByPair(TestWebAppFactory factory, int semester, int number) =>
        factory.Services.GetRequiredService<ILabRepository>().TryGetByPair(semester, number)
        ?? throw new InvalidOperationException($"Работы {semester}:{number} нет в хранилище тестового хоста.");

    /// <summary>Клиент с валидным по форме access-JWT для произвольного userId
    /// (пользователь может отсутствовать в хранилище — сценарий удалённой при
    /// живом access-токене учётки, зеркало requireSessionUser).</summary>
    public static HttpClient CreateSessionClient(TestWebAppFactory factory, Guid userId, string role)
    {
        var jwt = factory.Services
            .GetRequiredService<ITokenService>()
            .IssueAccessToken(userId, role);

        var client = CreateAnonymousClient(factory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={jwt}");
        return client;
    }

    public static async Task<HttpResponseMessage> PutJsonAsync(
        HttpClient client, string endpoint, string json) =>
        await client.PutAsync(endpoint, new StringContent(json, Encoding.UTF8, "application/json"));

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

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

    private static HttpClient CreateClient(TestWebAppFactory factory, User user)
    {
        var token = factory.Services
            .GetRequiredService<ITokenService>()
            .IssueAccessToken(user.Id, user.Role);

        var client = CreateAnonymousClient(factory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={token}");
        return client;
    }
}

/// <summary>
/// GET /submissions — ведомость (FR-021): страница студентов по 5 с двухколоночной
/// сортировкой, работы семестра number↑, записи только пар текущей страницы,
/// total/page, строгий semester и отказы query (только чтение — мутаций нет).
/// </summary>
public sealed class SubmissionsGridEndpointTests(SubmissionsApiFixture fixture) : IClassFixture<SubmissionsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Grid_FirstPage_DemoSeed_PagedSortedAndPairsOnly()
    {
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester=1&page=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        var root = body.RootElement;

        // students: страница по 5 (student01..05 — порядок fullName↑ при общем
        // префиксе «Иванов Иван Иванович …»).
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        var students = root.GetProperty("students");
        Assert.Equal(SubmissionsEndpointHarness.PageSize, students.GetArrayLength());
        var expectedFullNames = Enumerable.Range(1, 5)
            .Select(nn => $"Иванов Иван Иванович {nn:00}")
            .ToArray();
        Assert.Equal(
            expectedFullNames,
            students.EnumerateArray().Select(item => item.GetProperty("fullName").GetString()).ToArray());
        Assert.All(students.EnumerateArray(), item =>
        {
            Assert.True(Guid.TryParse(item.GetProperty("id").GetString(), out _));
            Assert.Equal(JsonValueKind.Object, item.ValueKind);
            Assert.Equal(2, item.EnumerateObject().Count());
        });

        // labs: 20 работ семестра 1, number↑, defenseRequired = чётный номер.
        var labs = root.GetProperty("labs").EnumerateArray().ToArray();
        Assert.Equal(20, labs.Length);
        Assert.Equal(
            Enumerable.Range(1, 20),
            labs.Select(item => item.GetProperty("number").GetInt32()));
        Assert.All(labs, item => Assert.Equal(
            item.GetProperty("number").GetInt32() % 2 == 0,
            item.GetProperty("defenseRequired").GetBoolean()));

        // submissions: только пары страницы 1 × работы семестра; в сиде —
        // student01×(1,2,3) и student02×1.
        var studentIds = students.EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .ToHashSet();
        var semesterLabIds = new HashSet<string?>(Enumerable.Range(1, 20)
            .Select(nn => SubmissionsEndpointHarness.LabByPair(_factory, 1, nn).Id.ToString()));
        var submissions = root.GetProperty("submissions").EnumerateArray().ToArray();
        Assert.Equal(4, submissions.Length);
        Assert.All(submissions, item =>
        {
            Assert.Contains(item.GetProperty("studentId").GetString(), studentIds);
            Assert.Contains(item.GetProperty("labId").GetString(), semesterLabIds);
            Assert.False(item.TryGetProperty("id", out _), "Ведомость — проекция без id записи.");
        });

        // total — полное число студентов группы; page — нормализованный номер.
        Assert.Equal(25, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Grid_SubmissionsMatchSeedDates()
    {
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        var student01 = SubmissionsEndpointHarness.UserByLogin(_factory, "student01").Id.ToString();
        var student02 = SubmissionsEndpointHarness.UserByLogin(_factory, "student02").Id.ToString();
        var lab1 = SubmissionsEndpointHarness.LabByPair(_factory, 1, 1).Id.ToString();
        var lab2 = SubmissionsEndpointHarness.LabByPair(_factory, 1, 2).Id.ToString();
        var lab3 = SubmissionsEndpointHarness.LabByPair(_factory, 1, 3).Id.ToString();
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester=1&page=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        var rows = body.RootElement.GetProperty("submissions").EnumerateArray()
            .Select(item => (
                StudentId: item.GetProperty("studentId").GetString(),
                LabId: item.GetProperty("labId").GetString(),
                SubmitDate: item.GetProperty("submitDate").GetString(),
                DefenseDate: item.GetProperty("defenseDate").GetString()))
            .ToArray();

        Assert.Contains((student01, lab1, "2026-09-01", "2026-09-11"), rows);
        Assert.Contains((student01, lab2, "2026-09-02", "2026-09-12"), rows);
        Assert.Contains((student01, lab3, "2026-09-03", null), rows);
        Assert.Contains((student02, lab1, "2026-09-01", null), rows);
    }

    [Theory]
    [InlineData(null)]           // semester отсутствует
    [InlineData("abc")]          // нечисловое
    [InlineData("2.5")]          // нецелое
    [InlineData("0")]            // ниже границы 1
    [InlineData("99")]           // выше Labs__MaxSemester=10
    public async Task Grid_StrictSemester_BadRequestWithFieldError(string? semester)
    {
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        var query = semester is null
            ? $"groupId={groupId}&page=1"
            : $"groupId={groupId}&semester={semester}&page=1";
        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?{query}");

        // AR-002/ISS-011: строгий контракт — 400, не 200 с пустыми массивами.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await SubmissionsEndpointHarness.MessageAsync(response));
        var errors = await SubmissionsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            new[] { "Семестр — число от 1 до 10" },
            SubmissionsEndpointHarness.ErrorOf(errors, "semester"));
    }

    [Theory]
    [InlineData("1")]   // нижняя граница
    [InlineData("10")]  // верхняя граница: семестр без работ — 200, не 400
    public async Task Grid_SemesterBoundaries_Accepted(string semester)
    {
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester={semester}&page=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        var labs = body.RootElement.GetProperty("labs");
        Assert.Equal(
            semester == "1" ? 20 : 0,
            labs.GetArrayLength());
    }

    [Fact]
    public async Task Grid_MissingGroupId_RequiredFieldError()
    {
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?semester=1&page=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await SubmissionsEndpointHarness.MessageAsync(response));
        var errors = await SubmissionsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            new[] { "Заполните поле" },
            SubmissionsEndpointHarness.ErrorOf(errors, "groupId"));
    }

    [Fact]
    public async Task Grid_MissingGroupAndSemester_BothErrorsAtOnce()
    {
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(SubmissionsEndpointHarness.GridEndpoint);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await SubmissionsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            new[] { "Семестр — число от 1 до 10" },
            SubmissionsEndpointHarness.ErrorOf(errors, "semester"));
        Assert.Equal(
            new[] { "Заполните поле" },
            SubmissionsEndpointHarness.ErrorOf(errors, "groupId"));
    }

    [Fact]
    public async Task Grid_UnknownGroup_NotFound()
    {
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);
        var unknownId = Guid.NewGuid();

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={unknownId}&semester=1&page=1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Группа не найдена", await SubmissionsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Grid_NonUuidGroup_NotFound()
    {
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId=not-a-uuid&semester=1&page=1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Группа не найдена", await SubmissionsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Grid_LastPage_FiveStudentsWithCorrectPage()
    {
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        // 25 студентов по 5 — последняя страница 5: student21..25.
        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester=1&page=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        var names = body.RootElement.GetProperty("students").EnumerateArray()
            .Select(item => item.GetProperty("fullName").GetString())
            .ToArray();
        Assert.Equal(
            Enumerable.Range(21, 5).Select(nn => $"Иванов Иван Иванович {nn:00}"),
            names);
        Assert.Equal(25, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(5, body.RootElement.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Grid_PageBeyondLast_EmptyItemsWithCorrectTotal()
    {
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester=1&page=6");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(0, body.RootElement.GetProperty("students").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("submissions").GetArrayLength());
        Assert.Equal(20, body.RootElement.GetProperty("labs").GetArrayLength());
        Assert.Equal(25, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(6, body.RootElement.GetProperty("page").GetInt32());
    }

    [Theory]
    [InlineData(214748365)] // зеркальная граница семейства PageSize=10 (в int помещается)
    [InlineData(214748366)] // зеркальная граница семейства PageSize=10 (для 5 ещё в int)
    [InlineData(429496731)] // первый page с int-переполнением (page-1)*5 (CR-001, PageSize=5)
    [InlineData(int.MaxValue)]
    public async Task Grid_HugePage_IntOffsetOverflowStillEmptyItems(int rawPage)
    {
        // CR-001: смещение (page-1)*PageSize в int для больших page заворачивается
        // в отрицательное (для pageSize=5 — с page ≥ 429 496 731) — Skip отдавал
        // ПЕРВУЮ страницу вместо пустой. Хвостовая страница обязана быть пустой
        // при корректном total, ответ эхом отдаёт нормализованный номер страницы.
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester=1&page={rawPage}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(rawPage, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(25, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("students").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("submissions").GetArrayLength());
        Assert.Equal(20, body.RootElement.GetProperty("labs").GetArrayLength());
    }

    [Fact]
    public async Task Grid_SecondPage_SubmissionsOnlyForSecondPageStudents()
    {
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        // Чередование страниц (FR-021): страница 1 содержит сид-сдачи
        // student01/02, страница 2 (student06..10) — пустые submissions,
        // при неизменных labs/total/page.
        using var first = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester=1&page=1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var firstBody = await SubmissionsEndpointHarness.ReadJsonAsync(first);
        Assert.Equal(4, firstBody.RootElement.GetProperty("submissions").GetArrayLength());

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester=1&page=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        var root = body.RootElement;
        // Тело содержит ВСЕ пять полей контракта (AR-003: page — серверное
        // расширение) и не содержит других.
        Assert.Equal(
            new HashSet<string> { "students", "labs", "submissions", "total", "page" },
            root.EnumerateObject().Select(property => property.Name).ToHashSet());
        var students = root.GetProperty("students").EnumerateArray().ToArray();
        Assert.Equal(SubmissionsEndpointHarness.PageSize, students.Length);
        Assert.Equal(
            Enumerable.Range(6, 5).Select(nn => $"Иванов Иван Иванович {nn:00}"),
            students.Select(item => item.GetProperty("fullName").GetString()));
        Assert.Equal(20, root.GetProperty("labs").GetArrayLength());
        Assert.Equal(0, root.GetProperty("submissions").GetArrayLength());
        Assert.Equal(25, root.GetProperty("total").GetInt32());
        Assert.Equal(2, root.GetProperty("page").GetInt32());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("2.5")]
    public async Task Grid_InvalidPage_NormalizedToOneWithoutEcho(string rawPage)
    {
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester=1&page={rawPage}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(SubmissionsEndpointHarness.PageSize, body.RootElement.GetProperty("students").GetArrayLength());
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
    }

    // ------------------------------------------------------------------
    // Роль-матрица GET /submissions (FR-022: submissions — только teacher)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Grid_AsStudent_Forbidden()
    {
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        using var client = SubmissionsEndpointHarness.CreateStudentClient(_factory, "student01");

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester=1&page=1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await SubmissionsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Grid_Anonymous_Unauthorized()
    {
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        using var client = SubmissionsEndpointHarness.CreateAnonymousClient(_factory);

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester=1&page=1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await SubmissionsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Grid_DeletedTeacherWithLiveAccess_Unauthorized()
    {
        // given: access-JWT минтован для teacher-роли, которой нет в хранилище
        // (удалена при живом access-токене — зеркало мока requireSessionUser).
        // Грид-запрос валиден целиком: отказ — из действия, а не из схемы.
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        using var client = SubmissionsEndpointHarness.CreateSessionClient(
            _factory, Guid.NewGuid(), UserRoles.Teacher);

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester=1&page=1");

        // then: 401 «Не авторизован» из действия (TryLoadCurrentUser), не 200
        // с ведомостью и не 400/404 query.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await SubmissionsEndpointHarness.MessageAsync(response));
    }
}

/// <summary>
/// PUT /submissions — upsert даты сдачи (FR-021): создание/полная замена обеих
/// дат (null = сброс), 400 дата-полей ДО 404 сущностей, ветки 404 студент →
/// работа, обновляет единственную пару этого класса (использует собственную
/// фикстуру — мутации не пересекаются с классами чтения).
/// </summary>
public sealed class SubmissionsUpsertEndpointTests(SubmissionsApiFixture fixture) : IClassFixture<SubmissionsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Put_CreatesRecord_ReturnsFullSubmissionWithTeacherMarks()
    {
        var teacherId = SubmissionsEndpointHarness.UserByLogin(_factory, "teacher").Id;
        var studentId = SubmissionsEndpointHarness.UserByLogin(_factory, "student05").Id;
        var labId = SubmissionsEndpointHarness.LabByPair(_factory, 1, 1).Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await SubmissionsEndpointHarness.PutJsonAsync(
            client,
            SubmissionsEndpointHarness.GridEndpoint,
            $$"""{"studentId":"{{studentId}}","labId":"{{labId}}","submitDate":"2026-09-20","defenseDate":null}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        var root = body.RootElement;

        // Полная запись (Null-формы не существует): id/studentId/labId/даты/
        // updatedAt/updatedBy.
        Assert.True(Guid.TryParse(root.GetProperty("id").GetString(), out _));
        Assert.Equal(studentId.ToString(), root.GetProperty("studentId").GetString());
        Assert.Equal(labId.ToString(), root.GetProperty("labId").GetString());
        Assert.Equal("2026-09-20", root.GetProperty("submitDate").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("defenseDate").ValueKind);
        Assert.Equal(teacherId.ToString(), root.GetProperty("updatedBy").GetString());
        AssertIso8601Utc(root.GetProperty("updatedAt").GetString());
    }

    [Fact]
    public async Task Put_GridReflectsCreatedSubmission()
    {
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        var studentId = SubmissionsEndpointHarness.UserByLogin(_factory, "student05").Id.ToString();
        var labId = SubmissionsEndpointHarness.LabByPair(_factory, 1, 1).Id.ToString();
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var put = await SubmissionsEndpointHarness.PutJsonAsync(
            client,
            SubmissionsEndpointHarness.GridEndpoint,
            $$"""{"studentId":"{{studentId}}","labId":"{{labId}}","submitDate":"2026-09-20","defenseDate":null}""");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        using var grid = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester=1&page=1");
        Assert.Equal(HttpStatusCode.OK, grid.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(grid);
        var rows = body.RootElement.GetProperty("submissions").EnumerateArray()
            .Select(item => (
                StudentId: item.GetProperty("studentId").GetString(),
                LabId: item.GetProperty("labId").GetString(),
                SubmitDate: item.GetProperty("submitDate").GetString()))
            .ToArray();

        Assert.Contains((studentId, labId, "2026-09-20"), rows);
    }

    [Fact]
    public async Task Put_ReplacesBothDates_ThenResetKeepsPairWithStableId()
    {
        var studentId = SubmissionsEndpointHarness.UserByLogin(_factory, "student01").Id;
        var labId = SubmissionsEndpointHarness.LabByPair(_factory, 1, 1).Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        // Полная замена обеих дат существующей сид-записи.
        using var replace = await SubmissionsEndpointHarness.PutJsonAsync(
            client,
            SubmissionsEndpointHarness.GridEndpoint,
            $$"""{"studentId":"{{studentId}}","labId":"{{labId}}","submitDate":"2026-09-30","defenseDate":"2026-10-11"}""");
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        string recordId;
        using (var body = await SubmissionsEndpointHarness.ReadJsonAsync(replace))
        {
            recordId = body.RootElement.GetProperty("id").GetString() ?? string.Empty;
            Assert.Equal("2026-09-30", body.RootElement.GetProperty("submitDate").GetString());
            Assert.Equal("2026-10-11", body.RootElement.GetProperty("defenseDate").GetString());
        }

        // Сброс: обе даты null, запись пары сохраняется, id стабилен.
        using var reset = await SubmissionsEndpointHarness.PutJsonAsync(
            client,
            SubmissionsEndpointHarness.GridEndpoint,
            $$"""{"studentId":"{{studentId}}","labId":"{{labId}}","submitDate":null,"defenseDate":null}""");
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var resetBody = await SubmissionsEndpointHarness.ReadJsonAsync(reset);
        Assert.Equal(recordId, resetBody.RootElement.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, resetBody.RootElement.GetProperty("submitDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, resetBody.RootElement.GetProperty("defenseDate").ValueKind);

        // Повторная ведомость отражает сброс: пара осталась с null-датами.
        var groupId = SubmissionsEndpointHarness.GroupByName(_factory, "ИК-221").Id;
        using var grid = await client.GetAsync(
            $"{SubmissionsEndpointHarness.GridEndpoint}?groupId={groupId}&semester=1&page=1");
        Assert.Equal(HttpStatusCode.OK, grid.StatusCode);
        using var gridBody = await SubmissionsEndpointHarness.ReadJsonAsync(grid);
        var row = gridBody.RootElement.GetProperty("submissions").EnumerateArray()
            .Single(item => item.GetProperty("studentId").GetString() == studentId.ToString()
                && item.GetProperty("labId").GetString() == labId.ToString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("submitDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("defenseDate").ValueKind);
    }

    [Fact]
    public async Task Put_NonContractDate_BadRequestBeforeEntityResolution()
    {
        // Неизвестные студент и работа — но 400 по дате РАНЬШЕ 404 (IF-012).
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await SubmissionsEndpointHarness.PutJsonAsync(
            client,
            SubmissionsEndpointHarness.GridEndpoint,
            $$"""{"studentId":"{{Guid.NewGuid()}}","labId":"{{Guid.NewGuid()}}","submitDate":"20.09.2026","defenseDate":null}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await SubmissionsEndpointHarness.MessageAsync(response));
        var errors = await SubmissionsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            new[] { "Дата должна быть строкой в формате ГГГГ-ММ-ДД" },
            SubmissionsEndpointHarness.ErrorOf(errors, "submitDate"));
        Assert.False(errors.TryGetProperty("defenseDate", out _));
    }

    [Fact]
    public async Task Put_CalendarInvalidAndNonStringDates_BothFieldErrors()
    {
        var studentId = SubmissionsEndpointHarness.UserByLogin(_factory, "student01").Id;
        var labId = SubmissionsEndpointHarness.LabByPair(_factory, 1, 1).Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        // '2026-02-30' — строгий формат 4-2-2, но календарно невалидна;
        // defenseDate — число (нестроковое значение).
        using var response = await SubmissionsEndpointHarness.PutJsonAsync(
            client,
            SubmissionsEndpointHarness.GridEndpoint,
            $$"""{"studentId":"{{studentId}}","labId":"{{labId}}","submitDate":"2026-02-30","defenseDate":42}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await SubmissionsEndpointHarness.ErrorsAsync(response);
        var dateText = "Дата должна быть строкой в формате ГГГГ-ММ-ДД";
        Assert.Equal(new[] { dateText }, SubmissionsEndpointHarness.ErrorOf(errors, "submitDate"));
        Assert.Equal(new[] { dateText }, SubmissionsEndpointHarness.ErrorOf(errors, "defenseDate"));
    }

    [Fact]
    public async Task Put_MissingDateField_BadRequest()
    {
        var studentId = SubmissionsEndpointHarness.UserByLogin(_factory, "student01").Id;
        var labId = SubmissionsEndpointHarness.LabByPair(_factory, 1, 1).Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        // Отсутствующее поле — тоже нарушение (контракт: обе даты всегда целиком).
        using var response = await SubmissionsEndpointHarness.PutJsonAsync(
            client,
            SubmissionsEndpointHarness.GridEndpoint,
            $$"""{"studentId":"{{studentId}}","labId":"{{labId}}","submitDate":null}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await SubmissionsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            new[] { "Дата должна быть строкой в формате ГГГГ-ММ-ДД" },
            SubmissionsEndpointHarness.ErrorOf(errors, "defenseDate"));
    }

    [Fact]
    public async Task Put_UnknownStudent_NotFound()
    {
        var labId = SubmissionsEndpointHarness.LabByPair(_factory, 1, 1).Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await SubmissionsEndpointHarness.PutJsonAsync(
            client,
            SubmissionsEndpointHarness.GridEndpoint,
            $$"""{"studentId":"{{Guid.NewGuid()}}","labId":"{{labId}}","submitDate":"2026-09-20","defenseDate":null}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Студент не найден", await SubmissionsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Put_TeacherAsStudent_NotFound()
    {
        // Пользователь существует, но role != student — тот же 404 (IF-012).
        var teacherId = SubmissionsEndpointHarness.UserByLogin(_factory, "teacher").Id;
        var labId = SubmissionsEndpointHarness.LabByPair(_factory, 1, 1).Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await SubmissionsEndpointHarness.PutJsonAsync(
            client,
            SubmissionsEndpointHarness.GridEndpoint,
            $$"""{"studentId":"{{teacherId}}","labId":"{{labId}}","submitDate":"2026-09-20","defenseDate":null}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Студент не найден", await SubmissionsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Put_UnknownLab_NotFound()
    {
        var studentId = SubmissionsEndpointHarness.UserByLogin(_factory, "student01").Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await SubmissionsEndpointHarness.PutJsonAsync(
            client,
            SubmissionsEndpointHarness.GridEndpoint,
            $$"""{"studentId":"{{studentId}}","labId":"{{Guid.NewGuid()}}","submitDate":"2026-09-20","defenseDate":null}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Лабораторная не найдена", await SubmissionsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Put_NonUuidStudent_NotFound()
    {
        var labId = SubmissionsEndpointHarness.LabByPair(_factory, 1, 1).Id;
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await SubmissionsEndpointHarness.PutJsonAsync(
            client,
            SubmissionsEndpointHarness.GridEndpoint,
            $$"""{"studentId":"garbage","labId":"{{labId}}","submitDate":"2026-09-20","defenseDate":null}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Студент не найден", await SubmissionsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Put_BrokenJson_BadRequestWithoutFieldErrors()
    {
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await SubmissionsEndpointHarness.PutJsonAsync(
            client, SubmissionsEndpointHarness.GridEndpoint, """{"studentId": oops""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await SubmissionsEndpointHarness.MessageAsync(response));
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        Assert.False(body.RootElement.TryGetProperty("errors", out _), "Битый JSON — 400 без errors (IF-001).");
    }

    // ------------------------------------------------------------------
    // Роль-матрица PUT /submissions (FR-022: submissions — только teacher)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Put_AsStudent_Forbidden()
    {
        using var client = SubmissionsEndpointHarness.CreateStudentClient(_factory, "student01");

        using var response = await SubmissionsEndpointHarness.PutJsonAsync(
            client,
            SubmissionsEndpointHarness.GridEndpoint,
            $$"""{"studentId":"{{Guid.NewGuid()}}","labId":"{{Guid.NewGuid()}}","submitDate":null,"defenseDate":null}""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await SubmissionsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Put_Anonymous_Unauthorized()
    {
        using var client = SubmissionsEndpointHarness.CreateAnonymousClient(_factory);

        using var response = await SubmissionsEndpointHarness.PutJsonAsync(
            client,
            SubmissionsEndpointHarness.GridEndpoint,
            $$"""{"studentId":"{{Guid.NewGuid()}}","labId":"{{Guid.NewGuid()}}","submitDate":null,"defenseDate":null}""");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await SubmissionsEndpointHarness.MessageAsync(response));
    }

    /// <summary>updatedAt — ISO-8601 UTC: суффикс Z и обратимый разбор (ADR-004).</summary>
    private static void AssertIso8601Utc(string? raw)
    {
        Assert.False(string.IsNullOrEmpty(raw));
        Assert.EndsWith("Z", raw, StringComparison.Ordinal);
        var parsed = DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
    }
}

/// <summary>
/// GET /me/submissions — свои сдачи (FR-021): hasGroup, работы семестра и
/// ТОЛЬКО свои записи (без studentId), строгий semester и роль-матрица
/// (только чтение — мутаций нет).
/// </summary>
public sealed class MySubmissionsEndpointTests(SubmissionsApiFixture fixture) : IClassFixture<SubmissionsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task My_InGroup_SemesterLabsAndOwnSubmissionsOnly()
    {
        using var client = SubmissionsEndpointHarness.CreateStudentClient(_factory, "student01");

        using var response = await client.GetAsync($"{SubmissionsEndpointHarness.MyEndpoint}?semester=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        var root = body.RootElement;
        Assert.True(root.GetProperty("hasGroup").GetBoolean());

        // Работы семестра 1: 20, number↑ (те же колонки, что у ведомости).
        var labs = root.GetProperty("labs").EnumerateArray().ToArray();
        Assert.Equal(20, labs.Length);
        Assert.Equal(
            Enumerable.Range(1, 20),
            labs.Select(item => item.GetProperty("number").GetInt32()));

        // Только свои сид-сдачи (labs 1..3), без studentId и без чужих строк.
        var semesterLabIds = new HashSet<string?>(Enumerable.Range(1, 20)
            .Select(nn => SubmissionsEndpointHarness.LabByPair(_factory, 1, nn).Id.ToString()));
        var submissions = root.GetProperty("submissions").EnumerateArray().ToArray();
        Assert.Equal(3, submissions.Length);
        Assert.All(submissions, item =>
        {
            Assert.Equal(
                new[] { "defenseDate", "labId", "submitDate" },
                item.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
            Assert.Contains(item.GetProperty("labId").GetString(), semesterLabIds);
        });
        // Порядок ListForStudentAndSemester не определён (IF-015): сравниваем
        // отсортированные последовательности, а не порядок вставки хранилища.
        Assert.Equal(
            new[] { "2026-09-01", "2026-09-02", "2026-09-03" },
            submissions
                .Select(item => item.GetProperty("submitDate").GetString())
                .OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public async Task My_WithoutGroup_EmptyResult()
    {
        // student31 — сид-студент без группы.
        Assert.Null(SubmissionsEndpointHarness.UserByLogin(_factory, "student31").GroupId);
        using var client = SubmissionsEndpointHarness.CreateStudentClient(_factory, "student31");

        using var response = await client.GetAsync($"{SubmissionsEndpointHarness.MyEndpoint}?semester=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        Assert.False(body.RootElement.GetProperty("hasGroup").GetBoolean());
        Assert.Equal(0, body.RootElement.GetProperty("labs").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("submissions").GetArrayLength());
    }

    [Theory]
    [InlineData("11")]   // выше Labs__MaxSemester=10
    [InlineData("abc")]  // нечисловое
    [InlineData("2.5")]  // нецелое
    public async Task My_StrictSemester_BadRequestWithFieldError(string semester)
    {
        using var client = SubmissionsEndpointHarness.CreateStudentClient(_factory, "student01");

        using var response = await client.GetAsync(
            $"{SubmissionsEndpointHarness.MyEndpoint}?semester={semester}");

        // ISS-011/AR-002: строгий контракт и для me — 400, не 200 с пустыми массивами.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await SubmissionsEndpointHarness.MessageAsync(response));
        var errors = await SubmissionsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            new[] { "Семестр — число от 1 до 10" },
            SubmissionsEndpointHarness.ErrorOf(errors, "semester"));
    }

    [Fact]
    public async Task My_MissingSemester_BadRequestWithFieldError()
    {
        using var client = SubmissionsEndpointHarness.CreateStudentClient(_factory, "student01");

        using var response = await client.GetAsync(SubmissionsEndpointHarness.MyEndpoint);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await SubmissionsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            new[] { "Семестр — число от 1 до 10" },
            SubmissionsEndpointHarness.ErrorOf(errors, "semester"));
    }

    [Fact]
    public async Task My_UpperSemesterBoundary_AcceptedWithEmptyLabs()
    {
        // Семестр 10 (граница) валиден: у студента он без работ — 200 с пустыми
        // массивами работ, но hasGroup по-прежнему true.
        using var client = SubmissionsEndpointHarness.CreateStudentClient(_factory, "student01");

        using var response = await client.GetAsync($"{SubmissionsEndpointHarness.MyEndpoint}?semester=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        Assert.True(body.RootElement.GetProperty("hasGroup").GetBoolean());
        Assert.Equal(0, body.RootElement.GetProperty("labs").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("submissions").GetArrayLength());
    }

    // ------------------------------------------------------------------
    // Роль-матрица GET /me/submissions (FR-022: только student)
    // ------------------------------------------------------------------

    [Fact]
    public async Task My_AsTeacher_Forbidden()
    {
        using var client = SubmissionsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync($"{SubmissionsEndpointHarness.MyEndpoint}?semester=1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await SubmissionsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task My_Anonymous_Unauthorized()
    {
        using var client = SubmissionsEndpointHarness.CreateAnonymousClient(_factory);

        using var response = await client.GetAsync($"{SubmissionsEndpointHarness.MyEndpoint}?semester=1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await SubmissionsEndpointHarness.MessageAsync(response));
    }
}
