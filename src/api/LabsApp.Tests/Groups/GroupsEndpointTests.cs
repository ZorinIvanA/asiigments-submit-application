using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Controllers;
using LabsApp.Domain.Entities;
using LabsApp.Hosting;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using LabsApp.Tests.Hosting;
using LabsApp.Tests.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.Tests.Groups;

// ============================================================================
// Эндпойнт-тесты контроллера Groups (C-008, IF-010; FR-019 + роль-матрица
// FR-022, представитель группы groups*). Сессии — DI-минт access-JWT через
// ITokenService хоста (ADR-015): POST /auth/login не используется. Данные —
// демо-сид (группы ИК-221×25 / ИК-222×5 / ИК-223×0, студенты student01..32);
// признак демо-набора закреплён ЯВНО в настройках хоста. Дополнительные
// группы/студенты сеются DI-ситом через репозитории своего сценария.
// GET /students?groupId=none (StudentsController, T-110) НЕ вызывается —
// видимость «освобождённых» студентов проверяется репозиторием, чьим
// потребителем будет тот эндпойнт.
// ============================================================================

/// <summary>Фикстура хоста с гарантированным демо-сидом (3 группы, 32 студента).</summary>
public sealed class GroupsApiFixture : IDisposable
{
    public TestWebAppFactory Factory { get; } = new(
        null,
        new Dictionary<string, string?> { [SeedOptions.DemoDataVariable] = "true" });

    public void Dispose() => Factory.Dispose();
}

/// <summary>Фикстура хоста без демо-набора: только сид-преподаватель, групп нет.</summary>
public sealed class NoDemoGroupsApiFixture : IDisposable
{
    public TestWebAppFactory Factory { get; } = new(
        null,
        new Dictionary<string, string?> { [SeedOptions.DemoDataVariable] = "false" });

    public void Dispose() => Factory.Dispose();
}

/// <summary>
/// Общий харнес groups-эндпойнтов: сессии (teacher/student/анонимно), DI-сид
/// групп и студентов, доступ к репозиториям хоста, чтение JSON-тел и конверта
/// ошибок IF-001.
/// </summary>
internal static class GroupsEndpointHarness
{
    public const string GroupsEndpoint = "/api/v1/groups";

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

    public static Group GroupByName(TestWebAppFactory factory, string name) =>
        factory.Services.GetRequiredService<IGroupRepository>().GetByName(name)
        ?? throw new InvalidOperationException($"Группы {name} нет в хранилище тестового хоста.");

    public static IGroupRepository Groups(TestWebAppFactory factory) =>
        factory.Services.GetRequiredService<IGroupRepository>();

    public static IUserRepository Users(TestWebAppFactory factory) =>
        factory.Services.GetRequiredService<IUserRepository>();

    public static User SeedStudent(
        TestWebAppFactory factory,
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
/// FR-019, перечень: демо-набор с вычисляемым studentCount, сортировка name↑
/// по правилам русской локали без учёта регистра (AR-005), пустой перечень.
/// </summary>
public sealed class GroupsListEndpointTests(GroupsApiFixture fixture) : IClassFixture<GroupsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task List_DemoSeed_ThreeGroupsOrderedWithComputedCounts()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(GroupsEndpointHarness.GroupsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await GroupsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Equal(3, body.RootElement.GetArrayLength());

        var names = new List<string>();
        var counts = new List<int>();
        foreach (var item in body.RootElement.EnumerateArray())
        {
            names.Add(item.GetProperty("name").GetString() ?? string.Empty);
            counts.Add(item.GetProperty("studentCount").GetInt32());
            Assert.False(string.IsNullOrEmpty(item.GetProperty("id").GetString()));
        }

        // Порядок name↑ ru; счётчики — вычисляемые по демо-сиду 25/5/0.
        Assert.Equal(new[] { "ИК-221", "ИК-222", "ИК-223" }, names);
        Assert.Equal(new[] { 25, 5, 0 }, counts);
    }

    [Fact]
    public async Task List_Student_Returns403ForbiddenEnvelope()
    {
        using var client = GroupsEndpointHarness.CreateStudentClient(_factory);

        using var response = await client.GetAsync(GroupsEndpointHarness.GroupsEndpoint);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await GroupsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task List_Anonymous_Returns401Envelope()
    {
        using var client = GroupsEndpointHarness.CreateAnonymousClient(_factory);

        using var response = await client.GetAsync(GroupsEndpointHarness.GroupsEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await GroupsEndpointHarness.MessageAsync(response));
    }
}

/// <summary>
/// FR-019/AR-005, сортировка перечня: name↑ по правилам русской локали без учёта
/// регистра. Выделенный хост (чистый fixture): создаются три группы, полный
/// перечень — ровно шесть групп.
/// </summary>
public sealed class GroupsListSortEndpointTests(GroupsApiFixture fixture) : IClassFixture<GroupsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task List_RuLocaleCaseInsensitive_NameAscending()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        // Три названия различают ordinal- и ru-порядок: при ordinal первым был бы
        // «Б-группа» (U+0411), при русской локали без учёта регистра — «а-группа».
        foreach (var name in new[] { "в-группа", "Б-группа", "а-группа" })
        {
            using var created = await client.PostAsync(
                GroupsEndpointHarness.GroupsEndpoint,
                GroupsEndpointHarness.Json($$"""{"name":"{{name}}"}"""));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var response = await client.GetAsync(GroupsEndpointHarness.GroupsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await GroupsEndpointHarness.ReadJsonAsync(response);
        var names = body.RootElement
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToArray();

        // а < Б < в (ci-русская локаль) раньше seeded-групп на «И».
        Assert.Equal(
            new[] { "а-группа", "Б-группа", "в-группа", "ИК-221", "ИК-222", "ИК-223" },
            names);
    }
}

/// <summary>FR-019, пустой перечень: без групп — 200 и пустой массив.</summary>
public sealed class GroupsEmptyListEndpointTests(NoDemoGroupsApiFixture fixture) : IClassFixture<NoDemoGroupsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task List_WithoutGroups_ReturnsEmptyArray()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(GroupsEndpointHarness.GroupsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await GroupsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Equal(0, body.RootElement.GetArrayLength());
    }
}

/// <summary>
/// FR-019, создание: трим, 201 с studentCount=0, дубликат lower(name) → 409,
/// границы длины и единый полевой текст, битый JSON без errors.
/// </summary>
public sealed class GroupsCreateEndpointTests(GroupsApiFixture fixture) : IClassFixture<GroupsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Create_TrimsName_ThenCaseInsensitiveDuplicate_Returns409()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var created = await client.PostAsync(
            GroupsEndpointHarness.GroupsEndpoint,
            GroupsEndpointHarness.Json("""{"name":"  ИК-224  "}"""));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = await GroupsEndpointHarness.ReadJsonAsync(created);
        Assert.False(string.IsNullOrEmpty(createdBody.RootElement.GetProperty("id").GetString()));
        Assert.Equal("ИК-224", createdBody.RootElement.GetProperty("name").GetString());
        Assert.Equal(0, createdBody.RootElement.GetProperty("studentCount").GetInt32());

        using var duplicate = await client.PostAsync(
            GroupsEndpointHarness.GroupsEndpoint,
            GroupsEndpointHarness.Json("""{"name":"ик-224"}"""));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(
            "Группа с таким названием уже существует",
            await GroupsEndpointHarness.MessageAsync(duplicate));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"name":null}""")]
    [InlineData("""{"name":123}""")]
    [InlineData("""{"name":""}""")]
    [InlineData("""{"name":"   "}""")]
    public async Task Create_NameOutsideBoundaries_SingleValidationText(string json)
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PostAsync(
            GroupsEndpointHarness.GroupsEndpoint,
            GroupsEndpointHarness.Json(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await GroupsEndpointHarness.MessageAsync(response));
        var errors = await GroupsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            new[] { "Название группы — от 1 до 100 символов" },
            GroupsEndpointHarness.ErrorOf(errors, "name"));
    }

    [Fact]
    public async Task Create_NameLengthBoundary_100Valid101Rejected()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        var boundaryName = new string('Я', 100);
        using var valid = await client.PostAsync(
            GroupsEndpointHarness.GroupsEndpoint,
            GroupsEndpointHarness.Json($$"""{"name":"{{boundaryName}}"}"""));
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);

        using var tooLong = await client.PostAsync(
            GroupsEndpointHarness.GroupsEndpoint,
            GroupsEndpointHarness.Json($$"""{"name":"{{boundaryName}}x"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var errors = await GroupsEndpointHarness.ErrorsAsync(tooLong);
        Assert.Equal(
            new[] { "Название группы — от 1 до 100 символов" },
            GroupsEndpointHarness.ErrorOf(errors, "name"));
    }

    [Fact]
    public async Task Create_SyntacticallyBrokenJson_Returns400MessageWithoutErrors()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PostAsync(
            GroupsEndpointHarness.GroupsEndpoint,
            GroupsEndpointHarness.Json("""{"name": broken"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Данные заполнены неверно", await GroupsEndpointHarness.MessageAsync(response));
        Assert.DoesNotContain("errors", rawBody, StringComparison.Ordinal);
    }
}

/// <summary>
/// FR-019, переименование: 200 с сохранением id/studentCount, отсутствие
/// самоконфликта, порядок 400 → 404 → 409, конфликт чужого имени.
/// </summary>
public sealed class GroupsUpdateEndpointTests(GroupsApiFixture fixture) : IClassFixture<GroupsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Update_Renames_Returns200WithSameIdAndComputedCount()
    {
        // Своя группа с одним студентом: studentCount должен пережить переименование.
        var group = GroupsEndpointHarness.GroupByName(_factory, "ИК-222");
        GroupsEndpointHarness.SeedStudent(_factory, "upd-count-1", "Переименование Тест Тестович", group.Id);
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PutAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{group.Id}",
            GroupsEndpointHarness.Json("""{"name":"  Переименованная-222  "}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await GroupsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(group.Id.ToString(), body.RootElement.GetProperty("id").GetString());
        Assert.Equal("Переименованная-222", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(6, body.RootElement.GetProperty("studentCount").GetInt32());
        Assert.NotNull(GroupsEndpointHarness.Groups(_factory).GetByName("переименованная-222"));
    }

    [Fact]
    public async Task Update_SameNameCiSelf_NoSelfConflict()
    {
        // Своя группа (fixture класса общий для тестов — seeded-группы не трогаем).
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);
        using var created = await client.PostAsync(
            GroupsEndpointHarness.GroupsEndpoint,
            GroupsEndpointHarness.Json("""{"name":"Сам-Конфликт"}"""));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = await GroupsEndpointHarness.ReadJsonAsync(created);
        var groupId = createdBody.RootElement.GetProperty("id").GetString();

        // Собственное имя в другом регистре конфликтом не считается (IF-010).
        using var response = await client.PutAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{groupId}",
            GroupsEndpointHarness.Json("""{"name":"сам-конфликт"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await GroupsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal("сам-конфликт", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Update_ForeignNameDuplicate_Returns409()
    {
        var group = GroupsEndpointHarness.GroupByName(_factory, "ИК-223");
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PutAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{group.Id}",
            GroupsEndpointHarness.Json("""{"name":"ик-221"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Группа с таким названием уже существует",
            await GroupsEndpointHarness.MessageAsync(response));
        // Имя группы не изменилось.
        Assert.Equal("ИК-223", GroupsEndpointHarness.GroupByName(_factory, "ИК-223").Name);
    }

    [Fact]
    public async Task Update_NonexistentGuid_Returns404GroupNotFound()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PutAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{Guid.NewGuid()}",
            GroupsEndpointHarness.Json("""{"name":"Нет такой группы"}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Группа не найдена", await GroupsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Update_NonGuidRouteValue_TreatedAsNotFound()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.PutAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/not-a-guid",
            GroupsEndpointHarness.Json("""{"name":"Не-uuid маршрут"}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Группа не найдена", await GroupsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Update_NonexistentIdInvalidBody_ValidationBeforeExistence()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        // Порядок 400 → 404: невалидное имя на несуществующем id даёт 400.
        using var response = await client.PutAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{Guid.NewGuid()}",
            GroupsEndpointHarness.Json("""{"name":""}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await GroupsEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            new[] { "Название группы — от 1 до 100 символов" },
            GroupsEndpointHarness.ErrorOf(errors, "name"));
    }

    [Fact]
    public async Task Update_SyntacticallyBrokenJson_Returns400MessageWithoutErrors()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        // Битый JSON тела PUT — 400 без errors (IF-001), причём раньше 404
        // несуществующего id (порядок IF-010).
        using var response = await client.PutAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{Guid.NewGuid()}",
            GroupsEndpointHarness.Json("""{"name": broken"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Данные заполнены неверно", await GroupsEndpointHarness.MessageAsync(response));
        Assert.DoesNotContain("errors", rawBody, StringComparison.Ordinal);
    }
}

/// <summary>
/// FR-019, удаление: 204, студенты сохраняются с groupId=null (видимы в
/// «без группы» — репозиторий фильтра groupId=none), повторные вызовы — 404.
/// </summary>
public sealed class GroupsDeleteEndpointTests(GroupsApiFixture fixture) : IClassFixture<GroupsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Delete_RemovesGroup_StudentsKeptWithoutGroup()
    {
        var group = GroupsEndpointHarness.GroupByName(_factory, "ИК-222");
        var detachedLogins = GroupsEndpointHarness.Users(_factory)
            .ListByGroup(group.Id)
            .Select(student => student.Login)
            .ToArray();
        Assert.Equal(5, detachedLogins.Length);
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.DeleteAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{group.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Группа исчезла из хранилища (и по ci-имени).
        Assert.Null(GroupsEndpointHarness.Groups(_factory).GetById(group.Id));
        Assert.Null(GroupsEndpointHarness.Groups(_factory).GetByName("ик-222"));

        // Учётные записи студентов живы и потеряли группу (groupId=null).
        var users = GroupsEndpointHarness.Users(_factory);
        foreach (var login in detachedLogins)
        {
            var student = users.GetByLogin(login);
            Assert.NotNull(student);
            Assert.Null(student.GroupId);
        }

        // Все «освобождённые» видимы выборкой «без группы» — бэкенд
        // GET /students?groupId=none (эндпойнт — зона T-110).
        var ungrouped = users.ListStudents(groupIdFilter: "none").Select(student => student.Login).ToArray();
        Assert.Subset(ungrouped.ToHashSet(), detachedLogins.ToHashSet());
        Assert.Equal(0, users.CountByGroup(group.Id));

        // Состав удалённой группы больше не читается.
        using var roster = await client.GetAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{group.Id}/students");
        Assert.Equal(HttpStatusCode.NotFound, roster.StatusCode);
    }

    [Fact]
    public async Task Delete_NonexistentGuid_Returns404GroupNotFound()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.DeleteAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Группа не найдена", await GroupsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Delete_NonGuidRouteValue_TreatedAsNotFound()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.DeleteAsync($"{GroupsEndpointHarness.GroupsEndpoint}/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Группа не найдена", await GroupsEndpointHarness.MessageAsync(response));
    }
}

/// <summary>
/// FR-019, состав группы: пагинация pageSize=10, нормализация page, страница
/// правее последней, двухколоночная сортировка fullName↑ затем login↑ (русская
/// локаль без учёта регистра), проекция StudentDto, 404 неизвестной группы.
/// </summary>
public sealed class GroupsStudentsEndpointTests(GroupsApiFixture fixture) : IClassFixture<GroupsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Students_SecondToThirdPage_PartialLastPageWithProjection()
    {
        var group = GroupsEndpointHarness.GroupByName(_factory, "ИК-221");
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{group.Id}/students?page=3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await GroupsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(25, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(3, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("pageSize").GetInt32());

        var items = body.RootElement.GetProperty("items");
        Assert.Equal(5, items.GetArrayLength());
        var logins = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            Assert.Equal(group.Id.ToString(), item.GetProperty("groupId").GetString());
            Assert.Equal("ИК-221", item.GetProperty("groupName").GetString());
            Assert.False(string.IsNullOrEmpty(item.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrEmpty(item.GetProperty("email").GetString()));
            logins.Add(item.GetProperty("login").GetString() ?? string.Empty);
        }

        // Полные ФИО сид-студентов различаются суффиксом «01»..«25» — сортировка
        // fullName↑ даёт студенты 21–25 на третьей странице.
        Assert.Equal(
            new[] { "student21", "student22", "student23", "student24", "student25" },
            logins);
    }

    [Fact]
    public async Task Students_DefaultPage_FirstPageSorted()
    {
        var group = GroupsEndpointHarness.GroupByName(_factory, "ИК-221");
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{group.Id}/students");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await GroupsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(25, body.RootElement.GetProperty("total").GetInt32());
        var logins = body.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("login").GetString())
            .ToArray();
        Assert.Equal(10, logins.Length);
        Assert.Equal("student01", logins[0]);
    }

    [Fact]
    public async Task Students_TwoColumnSort_FullNameThenLoginRuCi()
    {
        // Специальные ФИО/логины различают ru-ci и ordinal порядок:
        //  - «антонов …» раньше «Борисов …» по русской локали без учёта регистра
        //    (ordinal поставил бы «Борисов» (U+0411) первым);
        //  - при равном fullName логин «альфа-логин» раньше «Бета-логин»
        //    (ordinal снова дал бы обратное).
        var group = GroupsEndpointHarness.GroupByName(_factory, "ИК-223");
        GroupsEndpointHarness.SeedStudent(_factory, "g-borisov", "Борисов Борис", group.Id);
        GroupsEndpointHarness.SeedStudent(_factory, "Бета-логин", "антонов Арат", group.Id);
        GroupsEndpointHarness.SeedStudent(_factory, "альфа-логин", "антонов Арат", group.Id);
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{group.Id}/students");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await GroupsEndpointHarness.ReadJsonAsync(response);
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

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("2.5")]
    public async Task Students_InvalidPage_NormalizedToOne(string rawPage)
    {
        var group = GroupsEndpointHarness.GroupByName(_factory, "ИК-221");
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{group.Id}/students?page={Uri.EscapeDataString(rawPage)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await GroupsEndpointHarness.ReadJsonAsync(response);
        // Эхо некорректного значения запрещено — только нормализованный номер.
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(25, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Students_PageBeyondLast_EmptyItemsWithCorrectTotal()
    {
        var group = GroupsEndpointHarness.GroupByName(_factory, "ИК-221");
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{group.Id}/students?page=99");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await GroupsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(99, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(25, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData(214748365)] // последний page, чьё (page-1)*10 ещё помещается в int
    [InlineData(214748366)] // первый page с int-переполнением смещения (CR-001)
    [InlineData(int.MaxValue)]
    public async Task Students_HugePage_IntOffsetOverflowStillEmptyItems(int rawPage)
    {
        // CR-001: для page ≥ 214 748 366 смещение (page-1)*PageSize, посчитанное в
        // int, заворачивалось в отрицательное — Skip отдавал ПЕРВУЮ страницу вместо
        // пустой. Страница правее последней обязана быть пустой при корректном
        // total, ответ эхом отдаёт нормализованный номер страницы (IF-009/IF-010).
        var group = GroupsEndpointHarness.GroupByName(_factory, "ИК-221");
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{group.Id}/students?page={rawPage}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await GroupsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(rawPage, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(25, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Students_EmptyGroup_EmptyItemsZeroTotal()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);
        using var created = await client.PostAsync(
            GroupsEndpointHarness.GroupsEndpoint,
            GroupsEndpointHarness.Json("""{"name":"Пустая-группа"}"""));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = await GroupsEndpointHarness.ReadJsonAsync(created);
        var groupId = createdBody.RootElement.GetProperty("id").GetString();

        using var response = await client.GetAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{groupId}/students");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await GroupsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Students_NonexistentGuid_Returns404GroupNotFound()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{Guid.NewGuid()}/students");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Группа не найдена", await GroupsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Students_NonGuidRouteValue_TreatedAsNotFound()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/not-a-guid/students");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Группа не найдена", await GroupsEndpointHarness.MessageAsync(response));
    }
}

/// <summary>
/// FR-022 (представитель группы groups*): анонимно — 401, student — 403,
/// причём роль проверяется раньше валидации тела (400) и существования записи
/// (404); teacher — статус по бизнес-правилам.
/// </summary>
public sealed class GroupsRolesEndpointTests(GroupsApiFixture fixture) : IClassFixture<GroupsApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Post_Student_Returns403AndDoesNotCreate()
    {
        using var client = GroupsEndpointHarness.CreateStudentClient(_factory);

        using var response = await client.PostAsync(
            GroupsEndpointHarness.GroupsEndpoint,
            GroupsEndpointHarness.Json("""{"name":"Студенческая-группа"}"""));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await GroupsEndpointHarness.MessageAsync(response));
        Assert.Null(GroupsEndpointHarness.Groups(_factory).GetByName("студенческая-группа"));
    }

    [Fact]
    public async Task Post_StudentBrokenJson_403Before400()
    {
        using var client = GroupsEndpointHarness.CreateStudentClient(_factory);

        // Роль проверяется раньше разбора тела: битый JSON не превращает отказ в 400.
        using var response = await client.PostAsync(
            GroupsEndpointHarness.GroupsEndpoint,
            GroupsEndpointHarness.Json("""{"name": broken"""));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await GroupsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Put_StudentNonexistentIdInvalidBody_403Before400And404()
    {
        using var client = GroupsEndpointHarness.CreateStudentClient(_factory);

        // Роль раньше валидации тела (400) и поиска записи (404).
        using var response = await client.PutAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{Guid.NewGuid()}",
            GroupsEndpointHarness.Json("""{"name":""}"""));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await GroupsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Delete_StudentNonexistentId_403Before404()
    {
        using var client = GroupsEndpointHarness.CreateStudentClient(_factory);

        using var response = await client.DeleteAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await GroupsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Students_Student_Returns403()
    {
        var group = GroupsEndpointHarness.GroupByName(_factory, "ИК-221");
        using var client = GroupsEndpointHarness.CreateStudentClient(_factory);

        using var response = await client.GetAsync(
            $"{GroupsEndpointHarness.GroupsEndpoint}/{group.Id}/students");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Доступ запрещён", await GroupsEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task GetList_Teacher_BusinessStatus()
    {
        using var client = GroupsEndpointHarness.CreateTeacherClient(_factory);

        using var response = await client.GetAsync(GroupsEndpointHarness.GroupsEndpoint);

        // Своей ролью — не 401/403, а статус по бизнес-правилам (200).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>
/// Юнит-ветка 409-фолбэка PUT (IF-015 CONFLICT): гонка двух переименований на одно
/// имя не воспроизводима через публичный API детерминированно — pre-check
/// GetByName перекрывает её; атомарная замена репозитория сигнализирует конфликт
/// исключением StorageConflictException, которое контроллер обязан перевести в
/// 409 конверта (зеркало POST-ветки). Стаб репозитория — контролируемая заглушка
/// гонки, контроллер вызывается напрямую (unit-уровень, не эндпойнт).
/// </summary>
public sealed class GroupsUpdateRaceConflictUnitTests
{
    /// <summary>Декоратор: только Update бросает конфликт гонки, остальное — внутренний склад.</summary>
    private sealed class RaceConflictingGroupRepository(IGroupRepository inner) : IGroupRepository
    {
        public void Add(Group group) => inner.Add(group);

        public Group? GetById(Guid id) => inner.GetById(id);

        public Group? GetByName(string name) => inner.GetByName(name);

        public IReadOnlyList<Group> GetAll() => inner.GetAll();

        public IReadOnlyList<Group> List() => inner.List();

        public bool ExistsNameCi(string name) => inner.ExistsNameCi(name);

        public void Update(Group group) =>
            throw new StorageConflictException("Гонка двух PUT на одно имя (стаб).");

        public void Delete(Guid id) => inner.Delete(id);
    }

    [Fact]
    public async Task Update_RepositoryRaceConflict_Returns409Envelope()
    {
        var storage = TestStorage.Create();
        var group = TestEntities.Group("ИК-999");
        storage.Groups.Add(group);
        var controller = new GroupsController(
            new RaceConflictingGroupRepository(storage.Groups),
            storage.Users,
            new FakeTimeProvider());

        var teacher = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, UserRoles.Teacher) },
            "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = teacher },
        };
        controller.ControllerContext.HttpContext.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes("""{"name":"Переименование-гонки"}"""));

        var result = await controller.Update(group.Id.ToString(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var envelope = Assert.IsType<ErrorEnvelope>(conflict.Value);
        Assert.Equal("Группа с таким названием уже существует", envelope.Message);
    }
}
