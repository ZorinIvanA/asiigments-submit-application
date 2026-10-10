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

namespace LabsApp.Tests.Labs;

// ============================================================================
// Эндпойнт-тесты контроллеров Labs + Semesters (C-007, IF-009; FR-017/FR-018,
// представитель роли-матрицы FR-022). Сессии — DI-минт access-JWT через
// ITokenService хоста (ADR-015): POST /auth/login не используется. Данные —
// демо-сид (23 работы: семестр 1 №1–20 и семестр 2 №1–3; сдачи по (1,1), (1,2),
// (1,3) и (2,1)); признак демо-набора закреплён ЯВНО в настройках хоста, чтобы
// тесты не зависели от умолчаний TestWebAppFactory.
// ============================================================================

/// <summary>Фикстура хоста с гарантированным демо-сидом (23 работы).</summary>
public sealed class LabsApiFixture : IDisposable
{
    public TestWebAppFactory Factory { get; } = new(
        null,
        new Dictionary<string, string?> { [SeedOptions.DemoDataVariable] = "true" });

    public void Dispose() => Factory.Dispose();
}

/// <summary>Фикстура хоста без демо-набора (только сид-преподаватель, работ нет).</summary>
public sealed class NoDemoLabsApiFixture : IDisposable
{
    public TestWebAppFactory Factory { get; } = new(
        null,
        new Dictionary<string, string?> { [SeedOptions.DemoDataVariable] = "false" });

    public void Dispose() => Factory.Dispose();
}

/// <summary>
/// Общий харнес labs-эндпойнтов: сессии (teacher/student/анонимно), доступ к
/// репозиториям хоста, чтение JSON-тел и конверта ошибок IF-001.
/// </summary>
internal static class LabsEndpointHarness
{
    public const string LabsEndpoint = "/api/v1/labs";
    public const string SemestersEndpoint = "/api/v1/semesters";

    public static HttpClient CreateTeacherClient(TestWebAppFactory factory)
    {
        var teacher = factory.Services.GetRequiredService<IUserRepository>().GetByLogin("teacher");
        Assert.NotNull(teacher);
        return CreateSessionClient(factory, teacher.Id, UserRoles.Teacher);
    }

    public static HttpClient CreateStudentClient(TestWebAppFactory factory)
    {
        var student = factory.Services.GetRequiredService<IUserRepository>().GetByLogin("student01");
        Assert.NotNull(student);
        return CreateSessionClient(factory, student.Id, UserRoles.Student);
    }

    public static HttpClient CreateAnonymousClient(TestWebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    public static Lab LabByPair(TestWebAppFactory factory, int semester, int number) =>
        factory.Services.GetRequiredService<ILabRepository>().TryGetByPair(semester, number)
        ?? throw new InvalidOperationException($"Работы {semester}:{number} нет в сид-данных тестового хоста.");

    public static IReadOnlyList<Submission> SubmissionsOfLab(TestWebAppFactory factory, Guid labId) =>
        factory.Services.GetRequiredService<ISubmissionRepository>().ListByLabIds([labId]);

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

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

    private static HttpClient CreateSessionClient(TestWebAppFactory factory, Guid userId, string role)
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
/// FR-017, список: дефолтная страница, мягкий semester (ASM-018), двухколоночная
/// сортировка, нормализация page и страница правее последней.
/// </summary>
public sealed class LabsListEndpointTests(LabsApiFixture fixture) : IClassFixture<LabsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task List_DefaultSeed_FirstPageContractShape()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(LabsEndpointHarness.LabsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(23, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("pageSize").GetInt32());
        var items = body.RootElement.GetProperty("items");
        Assert.Equal(10, items.GetArrayLength());
        // Дефолтная сортировка semester↑,number↑: первый элемент — (1,1).
        Assert.Equal(1, items[0].GetProperty("semester").GetInt32());
        Assert.Equal(1, items[0].GetProperty("number").GetInt32());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("2.5")]
    public async Task List_SoftSemester_NonIntegerNoFilter(string rawSemester)
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{LabsEndpointHarness.LabsEndpoint}?semester={Uri.EscapeDataString(rawSemester)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(23, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task List_OutOfRangeSemester_EmptyResultNotBadRequest()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync($"{LabsEndpointHarness.LabsEndpoint}?semester=99");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task List_Semester2_NumberDescending()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{LabsEndpointHarness.LabsEndpoint}?semester=2&sortField=number&sortDir=desc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(3, body.RootElement.GetProperty("total").GetInt32());
        var numbers = body.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("number").GetInt32())
            .ToArray();
        Assert.Equal(new[] { 3, 2, 1 }, numbers);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("2.5")]
    public async Task List_InvalidPage_NormalizedToOne(string rawPage)
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{LabsEndpointHarness.LabsEndpoint}?page={Uri.EscapeDataString(rawPage)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        // Эхо некорректного значения запрещено — только нормализованный номер.
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(23, body.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task List_PageBeyondLast_EmptyItemsWithCorrectTotal()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync($"{LabsEndpointHarness.LabsEndpoint}?page=99");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(23, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(99, body.RootElement.GetProperty("page").GetInt32());
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
        // total (IF-009), ответ эхом отдаёт нормализованный номер страницы.
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync($"{LabsEndpointHarness.LabsEndpoint}?page={rawPage}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(rawPage, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(23, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_SortFieldOutsideDictionary_NormalizesToDefaultSemesterAscending()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        // «xyz» вне словаря ('number'|'semester') → дефолт semester↑,number↑, без ошибки.
        using var response = await client.GetAsync(
            $"{LabsEndpointHarness.LabsEndpoint}?sortField=xyz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(23, body.RootElement.GetProperty("total").GetInt32());
        var pairs = Pairs(body.RootElement.GetProperty("items"));
        Assert.Equal(new[] { (1, 1), (1, 2), (1, 3) }, pairs.Take(3).ToArray());
    }

    [Fact]
    public async Task List_SortDirOutsideDictionary_NormalizesToAscending()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        // «sideways» вне словаря ('asc'|'desc') → asc: number↑,semester↑ →
        // сначала (1,1), затем (2,1) — вторичный semester всегда asc.
        using var response = await client.GetAsync(
            $"{LabsEndpointHarness.LabsEndpoint}?sortField=number&sortDir=sideways");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(new[] { (1, 1), (2, 1), (1, 2) }, Pairs(body.RootElement.GetProperty("items")).Take(3).ToArray());
    }

    [Fact]
    public async Task List_GarbageSortFieldWithDesc_PrimarySemesterDescendingSecondaryNumberAscending()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        // sortField вне словаря → semester; sortDir=desc валиден → semester↓,
        // вторичный number всегда asc: (2,1), (2,2), (2,3), затем (1,1)…
        using var response = await client.GetAsync(
            $"{LabsEndpointHarness.LabsEndpoint}?sortField=xyz&sortDir=desc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(
            new[] { (2, 1), (2, 2), (2, 3), (1, 1) },
            Pairs(body.RootElement.GetProperty("items")).Take(4).ToArray());
    }

    /// <summary>Пары (semester, number) элементов страницы (клон: после dispose документа).</summary>
    private static (int Semester, int Number)[] Pairs(JsonElement items) =>
        [.. items.EnumerateArray().Select(item
            => (item.GetProperty("semester").GetInt32(), item.GetProperty("number").GetInt32()))];
}

/// <summary>FR-017, чтение одной работы: 200 LabDto и 404 NOT_FOUND_LAB.</summary>
public sealed class LabsGetByIdEndpointTests(LabsApiFixture fixture) : IClassFixture<LabsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task GetById_SeededLab_ReturnsDto()
    {
        var lab = LabsEndpointHarness.LabByPair(_factory, 1, 1);
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync($"{LabsEndpointHarness.LabsEndpoint}/{lab.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(lab.Id.ToString(), body.RootElement.GetProperty("id").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("semester").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("number").GetInt32());
        Assert.Equal(lab.Content, body.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task GetById_NonexistentGuid_Returns404NotFoundText()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync($"{LabsEndpointHarness.LabsEndpoint}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Лабораторная не найдена", await LabsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task GetById_NonGuidRouteValue_TreatedAsNotFound()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync($"{LabsEndpointHarness.LabsEndpoint}/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Лабораторная не найдена", await LabsEndpointHarness.MessageAsync(response));
    }
}

/// <summary>
/// FR-017, создание: нормализация после валидации (строки-цифры number, ''→null,
/// строго boolean defenseRequired), дубликат пары, границы semester и длины
/// assignmentUrl, полевая 400 «все ошибки сразу», битый JSON без errors.
/// </summary>
public sealed class LabsCreateEndpointTests(LabsApiFixture fixture) : IClassFixture<LabsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Create_ValidInput_Returns201WithNormalizedDto()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json(
                """{"number":21,"semester":1,"content":"Новая","assignmentUrl":"","defenseRequired":true}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        var id = body.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id));
        Assert.Equal(21, body.RootElement.GetProperty("number").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("semester").GetInt32());
        Assert.Equal("Новая", body.RootElement.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("assignmentUrl").ValueKind);
        Assert.True(body.RootElement.GetProperty("defenseRequired").GetBoolean());

        // Созданная запись доступна по id (глубокая проверка POST через GET).
        using var readBack = await client.GetAsync($"{LabsEndpointHarness.LabsEndpoint}/{id}");
        Assert.Equal(HttpStatusCode.OK, readBack.StatusCode);
    }

    [Fact]
    public async Task Create_NumberAndSemesterAsDigitStrings_NormalizedToIntegers()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json(
                """{"number":"22","semester":"1","content":"Строковый номер","assignmentUrl":null,"defenseRequired":false}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(22, body.RootElement.GetProperty("number").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("semester").GetInt32());
    }

    [Fact]
    public async Task Create_NumberAsString21_IsAcceptedWithInteger201()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        // Строковый number «21» (семестр 2: пара (2,21) вне сид-данных) → 201 c int.
        using var response = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json(
                """{"number":"21","semester":2,"content":"Строковый номер 21","assignmentUrl":null,"defenseRequired":false}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("id").GetString()));
        Assert.Equal(21, body.RootElement.GetProperty("number").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("semester").GetInt32());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("1.5")]
    [InlineData("abc")]
    public async Task Create_NumberInvalidValue_ReturnsNumberFieldError(string rawNumber)
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json(
                $$"""{"number":"{{rawNumber}}","semester":1,"content":"Невалидный номер","assignmentUrl":null,"defenseRequired":false}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await LabsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            new[] { "Номер должен быть положительным числом" },
            LabsEndpointHarness.ErrorOf(errors, "number"));
    }

    [Theory]
    [InlineData("\"yes\"", 701)] // строка вместо boolean
    [InlineData("1", 702)] // число вместо boolean
    [InlineData("null", 703)] // литерал null — значения нет
    public async Task Create_DefenseRequiredNotStrictBoolean_Returns400AndNotCreated(
        string rawDefense, int number)
    {
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        // defenseRequired — строго boolean (TS-114): нестроковое/нечисловое/null
        // значения отклоняются полевой валидацией, работа не создаётся.
        using var response = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json(
                $$"""{"number":{{number}},"semester":3,"content":"Строго boolean","assignmentUrl":null,"defenseRequired":{{rawDefense}}}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await LabsEndpointHarness.ErrorsAsync(response);
        Assert.NotEmpty(LabsEndpointHarness.ErrorOf(errors, "defenseRequired"));
        Assert.Null(labs.TryGetByPair(semester: 3, number: number));
    }

    [Fact]
    public async Task Create_MissingDefenseRequired_Returns400AndNotCreated()
    {
        // TS-114 (3): тело без поля defenseRequired вовсе — 400 (POST требует
        // явного boolean), работа не создана.
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json(
                """{"number":704,"semester":3,"content":"Строго boolean","assignmentUrl":null}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await LabsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(new[] { "Заполните поле" }, LabsEndpointHarness.ErrorOf(errors, "defenseRequired"));
        Assert.Null(labs.TryGetByPair(semester: 3, number: 704));
    }

    [Fact]
    public async Task Create_ExistingPair_Returns409DuplicateText()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json(
                """{"number":1,"semester":1,"content":"Дубликат пары","assignmentUrl":null,"defenseRequired":false}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Лабораторная с таким номером уже есть в семестре",
            await LabsEndpointHarness.MessageAsync(response));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(0, false)]
    [InlineData(11, false)]
    public async Task Create_SemesterBoundaries_TemplateTextFromMaxSemester(
        int semester, bool expectedCreated)
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json(
                $$"""{"number":{{30 + semester}},"semester":{{semester}},"content":"Граница семестра","assignmentUrl":null,"defenseRequired":false}"""));

        if (expectedCreated)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var errors = await LabsEndpointHarness.ErrorsAsync(response);
            Assert.Equal(
                new[] { "Семестр — число от 1 до 10" },
                LabsEndpointHarness.ErrorOf(errors, "semester"));
        }
    }

    [Fact]
    public async Task Create_AssignmentUrlLengthBoundary_1000Valid1001Rejected()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);
        const string prefix = "https://example.com/";

        // Ровно 1000 символов после трима — валидно (граница включительно).
        var validUrl = prefix + new string('a', 1000 - prefix.Length);
        using var valid = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json(
                $$"""{"number":41,"semester":1,"content":"Граница ссылки","assignmentUrl":"{{validUrl}}","defenseRequired":false}"""));
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);

        // 1001 символ — 400 с серверным текстом lab.url.length.
        var tooLongUrl = validUrl + "b";
        using var tooLong = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json(
                $$"""{"number":42,"semester":1,"content":"Граница ссылки","assignmentUrl":"{{tooLongUrl}}","defenseRequired":false}"""));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var errors = await LabsEndpointHarness.ErrorsAsync(tooLong);
        Assert.Equal(
            new[] { "Ссылка — не более 1000 символов" },
            LabsEndpointHarness.ErrorOf(errors, "assignmentUrl"));
    }

    [Fact]
    public async Task Create_AllFieldsInvalid_ReturnsAllFieldErrorsAtOnce()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json(
                """{"number":"abc","semester":0,"content":"   ","assignmentUrl":"ftp://example.com"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await LabsEndpointHarness.MessageAsync(response));
        var errors = await LabsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            new[] { "Номер должен быть положительным числом" },
            LabsEndpointHarness.ErrorOf(errors, "number"));
        Assert.Equal(
            new[] { "Семестр — число от 1 до 10" },
            LabsEndpointHarness.ErrorOf(errors, "semester"));
        Assert.Equal(
            new[] { "Заполните поле" },
            LabsEndpointHarness.ErrorOf(errors, "content"));
        Assert.Equal(
            new[] { "Ссылка должна начинаться с http:// или https://" },
            LabsEndpointHarness.ErrorOf(errors, "assignmentUrl"));
    }

    [Fact]
    public async Task Create_MissingRequiredFields_ReturnsRequiredErrors()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await LabsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(new[] { "Заполните поле" }, LabsEndpointHarness.ErrorOf(errors, "number"));
        Assert.Equal(new[] { "Заполните поле" }, LabsEndpointHarness.ErrorOf(errors, "semester"));
        Assert.Equal(new[] { "Заполните поле" }, LabsEndpointHarness.ErrorOf(errors, "content"));
        Assert.False(errors.TryGetProperty("assignmentUrl", out _));
    }

    [Fact]
    public async Task Create_SyntacticallyBrokenJson_Returns400MessageWithoutErrors()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json("""{"number": broken"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Данные заполнены неверно", await LabsEndpointHarness.MessageAsync(response));
        Assert.DoesNotContain("errors", rawBody, StringComparison.Ordinal);
    }
}

/// <summary>
/// FR-017, правка и удаление: self-конфликт PUT, порядок 400→404→409,
/// конфликт чужой пары, каскадное удаление сдач.
/// </summary>
public sealed class LabsUpdateDeleteEndpointTests(LabsApiFixture fixture) : IClassFixture<LabsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Update_SamePairSelfConflict_Returns200KeepsId()
    {
        var lab = LabsEndpointHarness.LabByPair(_factory, 1, 5);
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PutAsync(
            $"{LabsEndpointHarness.LabsEndpoint}/{lab.Id}",
            LabsEndpointHarness.Json(
                $$"""{"number":5,"semester":1,"content":"Обновлённое содержание","assignmentUrl":null,"defenseRequired":true}"""));

        // Собственная пара (1,5) конфликтом не считается.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(lab.Id.ToString(), body.RootElement.GetProperty("id").GetString());
        Assert.Equal("Обновлённое содержание", body.RootElement.GetProperty("content").GetString());
        Assert.True(body.RootElement.GetProperty("defenseRequired").GetBoolean());
    }

    [Fact]
    public async Task Update_NonexistentIdValidBody_Returns404()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PutAsync(
            $"{LabsEndpointHarness.LabsEndpoint}/{Guid.NewGuid()}",
            LabsEndpointHarness.Json(
                """{"number":51,"semester":1,"content":"Нет такой работы","assignmentUrl":null,"defenseRequired":false}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Лабораторная не найдена", await LabsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Update_NonexistentIdInvalidBody_ValidationBeforeExistence()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        // Порядок 400 → 404: невалидное тело на несуществующем id даёт 400.
        using var response = await client.PutAsync(
            $"{LabsEndpointHarness.LabsEndpoint}/{Guid.NewGuid()}",
            LabsEndpointHarness.Json(
                """{"number":52,"semester":0,"content":"Порядок отказов","assignmentUrl":null,"defenseRequired":false}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await LabsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            new[] { "Семестр — число от 1 до 10" },
            LabsEndpointHarness.ErrorOf(errors, "semester"));
    }

    [Fact]
    public async Task Update_DefenseRequiredNonBoolean_Returns400RecordUnchanged()
    {
        // PUT наследует строгость boolean при наличии поля (TS-114): строка — 400.
        // Сид (1,2): defenseRequired=true — отказ обязан НЕ изменить запись.
        var lab = LabsEndpointHarness.LabByPair(_factory, 1, 2);
        Assert.True(lab.DefenseRequired);
        var contentBefore = lab.Content;
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PutAsync(
            $"{LabsEndpointHarness.LabsEndpoint}/{lab.Id}",
            LabsEndpointHarness.Json(
                """{"number":2,"semester":1,"content":"Не применённое содержание","assignmentUrl":null,"defenseRequired":"yes"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await LabsEndpointHarness.ErrorsAsync(response);
        Assert.NotEmpty(LabsEndpointHarness.ErrorOf(errors, "defenseRequired"));

        var stored = _factory.Services.GetRequiredService<ILabRepository>().GetById(lab.Id);
        Assert.NotNull(stored);
        Assert.True(stored.DefenseRequired);
        Assert.Equal(contentBefore, stored.Content);
    }

    [Fact]
    public async Task Update_MissingDefenseRequired_ToleratedNormalizedToFalse()
    {
        // Контракт TS-097: PUT-тело без defenseRequired валидно — нормализация
        // в false (создание требует явного boolean, правка допускает отсутствие).
        // Сид (1,2): defenseRequired=true — гард предусловия делает финальный
        // false наблюдаемой НОРМАЛИЗАЦИЕЙ, а не унаследованным значением
        // (реворк CR-004: сид-работа (1,5) имела false до запроса).
        var lab = LabsEndpointHarness.LabByPair(_factory, 1, 2);
        Assert.True(lab.DefenseRequired);
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PutAsync(
            $"{LabsEndpointHarness.LabsEndpoint}/{lab.Id}",
            LabsEndpointHarness.Json(
                """{"number":2,"semester":1,"content":"Обновление без поля","assignmentUrl":null}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal("Обновление без поля", body.RootElement.GetProperty("content").GetString());
        Assert.False(body.RootElement.GetProperty("defenseRequired").GetBoolean());
        var stored = _factory.Services.GetRequiredService<ILabRepository>().GetById(lab.Id);
        Assert.NotNull(stored);
        Assert.False(stored.DefenseRequired);
    }

    [Fact]
    public async Task Update_ForeignPairConflict_Returns409()
    {
        // Свои записи вместо сид-пар: тест не зависит от порядка тестов класса
        // (каскадное удаление сид-работы (1,1) не должно ломать сценарий).
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);
        using var first = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json(
                """{"number":61,"semester":3,"content":"Конфликтная пара — источник","assignmentUrl":null,"defenseRequired":false}"""));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await client.PostAsync(
            LabsEndpointHarness.LabsEndpoint,
            LabsEndpointHarness.Json(
                """{"number":62,"semester":3,"content":"Конфликтная пара — занятая","assignmentUrl":null,"defenseRequired":false}"""));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        string? sourceId;
        using (var firstBody = await LabsEndpointHarness.ReadJsonAsync(first))
        {
            sourceId = firstBody.RootElement.GetProperty("id").GetString();
        }

        using var response = await client.PutAsync(
            $"{LabsEndpointHarness.LabsEndpoint}/{sourceId}",
            LabsEndpointHarness.Json(
                """{"number":62,"semester":3,"content":"Чужая пара","assignmentUrl":null,"defenseRequired":false}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Лабораторная с таким номером уже есть в семестре",
            await LabsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Delete_SeededLabWithSubmission_CascadesSubmissions()
    {
        var lab = LabsEndpointHarness.LabByPair(_factory, 1, 1);
        // Предусловие: по работе (1,1) есть сид-сдача (каскаду есть что удалять).
        Assert.NotEmpty(LabsEndpointHarness.SubmissionsOfLab(_factory, lab.Id));
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.DeleteAsync($"{LabsEndpointHarness.LabsEndpoint}/{lab.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(LabsEndpointHarness.SubmissionsOfLab(_factory, lab.Id));
        using var readBack = await client.GetAsync($"{LabsEndpointHarness.LabsEndpoint}/{lab.Id}");
        Assert.Equal(HttpStatusCode.NotFound, readBack.StatusCode);
    }

    [Fact]
    public async Task Delete_NonexistentId_Returns404()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.DeleteAsync($"{LabsEndpointHarness.LabsEndpoint}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Лабораторная не найдена", await LabsEndpointHarness.MessageAsync(response));
    }
}

/// <summary>
/// FR-022 (представитель роли-матрицы, labs*): анонимно — 401, student — 403
/// (раньше 400/404), teacher — статус по бизнес-правилам.
/// </summary>
public sealed class LabsRolesEndpointTests(LabsApiFixture fixture) : IClassFixture<LabsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task GetList_Anonymous_Returns401Envelope()
    {
        using var client = LabsEndpointHarness.CreateAnonymousClient(_factory);

        using var response = await client.GetAsync(LabsEndpointHarness.LabsEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await LabsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task GetList_Student_Returns403ForbiddenEnvelope()
    {
        using var client = LabsEndpointHarness.CreateStudentClient(_factory);

        using var response = await client.GetAsync(LabsEndpointHarness.LabsEndpoint);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await LabsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Put_StudentNonexistentIdInvalidBody_403Before400And404()
    {
        using var client = LabsEndpointHarness.CreateStudentClient(_factory);

        // Роль проверяется раньше валидации тела и существования записи (FR-022).
        using var response = await client.PutAsync(
            $"{LabsEndpointHarness.LabsEndpoint}/{Guid.NewGuid()}",
            LabsEndpointHarness.Json(
                """{"number":53,"semester":0,"content":"Роль раньше тела","assignmentUrl":null,"defenseRequired":false}"""));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await LabsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Delete_Student_Returns403()
    {
        var lab = LabsEndpointHarness.LabByPair(_factory, 1, 1);
        using var client = LabsEndpointHarness.CreateStudentClient(_factory);

        using var response = await client.DeleteAsync($"{LabsEndpointHarness.LabsEndpoint}/{lab.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await LabsEndpointHarness.MessageAsync(response));
        // Работа не удалена — отказ произошёл до бизнес-логики.
        Assert.NotNull(_factory.Services.GetRequiredService<ILabRepository>().GetById(lab.Id));
    }

    [Fact]
    public async Task GetList_Teacher_BusinessStatus()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(LabsEndpointHarness.LabsEndpoint);

        // Своей ролью — не 401/403, а статус по бизнес-правилам (200).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>FR-018: GET /semesters — distinct по возрастанию, любая роль; анонимно 401.</summary>
public sealed class SemestersEndpointTests(LabsApiFixture fixture) : IClassFixture<LabsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Semesters_UnderStudent_DistinctAscending()
    {
        using var client = LabsEndpointHarness.CreateStudentClient(_factory);

        using var response = await client.GetAsync(LabsEndpointHarness.SemestersEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Equal(
            new[] { 1, 2 },
            body.RootElement.EnumerateArray().Select(item => item.GetInt32()).ToArray());
    }

    [Fact]
    public async Task Semesters_UnderTeacher_Returns200()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(LabsEndpointHarness.SemestersEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Semesters_Anonymous_Returns401()
    {
        using var client = LabsEndpointHarness.CreateAnonymousClient(_factory);

        using var response = await client.GetAsync(LabsEndpointHarness.SemestersEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await LabsEndpointHarness.MessageAsync(response));
    }
}

/// <summary>FR-018, ветка пустого перечня: без работ — 200 и пустой массив.</summary>
public sealed class SemestersEmptyEndpointTests(NoDemoLabsApiFixture fixture) : IClassFixture<NoDemoLabsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Semesters_WithoutLabs_ReturnsEmptyArray()
    {
        using var client = LabsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(LabsEndpointHarness.SemestersEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await LabsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Equal(0, body.RootElement.GetArrayLength());
    }
}
