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

namespace LabsApp.Tests.Profile;

// ============================================================================
// Эндпойнт-тесты ProfileController (C-011, IF-013; FR-015 + роль-матрица
// FR-022 role=user). Сессии — DI-минт access-JWT через ITokenService хоста
// (ADR-015): POST /auth/login не используется. GET-сценарии — демо-сид
// (student01 в ИК-221); PUT-сценарии — собственные пользователи DI-сидом
// через репозитории (изолированный хост без демо-набора).
// ============================================================================

/// <summary>Фикстура хоста с демо-сидом (student01 в ИК-221) для GET-сценариев.</summary>
public sealed class ProfileGetApiFixture : IDisposable
{
    public TestWebAppFactory Factory { get; } = new(
        null,
        new Dictionary<string, string?> { [SeedOptions.DemoDataVariable] = "true" });

    public void Dispose() => Factory.Dispose();
}

/// <summary>Фикстура хоста без демо-набора: PUT-сценарии сидуют собственных пользователей.</summary>
public sealed class ProfilePutApiFixture : IDisposable
{
    public TestWebAppFactory Factory { get; } = new(
        null,
        new Dictionary<string, string?> { [SeedOptions.DemoDataVariable] = "false" });

    public void Dispose() => Factory.Dispose();
}

/// <summary>
/// Харнес profile-эндпойнтов: сессии (минт access-cookie), DI-сид групп и
/// пользователей, чтение JSON-тел и конверта ошибок IF-001.
/// </summary>
internal static class ProfileEndpointHarness
{
    public const string ProfileEndpoint = "/api/v1/me/profile";

    public static HttpClient CreateAnonymousClient(TestWebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    public static HttpClient CreateSessionClient(TestWebAppFactory factory, Guid userId, string role)
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

    public static User SeedUser(
        TestWebAppFactory factory,
        string login,
        string? email = null,
        string? fullName = null,
        string role = UserRoles.Student,
        Guid? groupId = null)
    {
        var repository = factory.Services.GetRequiredService<IUserRepository>();
        repository.Add(TestEntities.User(login, email, role, groupId, fullName));
        return repository.GetByLogin(login)
            ?? throw new InvalidOperationException($"Пользователь {login} не сохранился при DI-сиде.");
    }

    public static Group SeedGroup(TestWebAppFactory factory, string name)
    {
        var repository = factory.Services.GetRequiredService<IGroupRepository>();
        repository.Add(TestEntities.Group(name));
        return repository.GetByName(name)
            ?? throw new InvalidOperationException($"Группа {name} не сохранилась при DI-сиде.");
    }

    public static void RenameGroup(TestWebAppFactory factory, Group group, string newName)
    {
        group.Name = newName;
        factory.Services.GetRequiredService<IGroupRepository>().Update(group);
    }

    public static User StoredUser(TestWebAppFactory factory, Guid id) =>
        factory.Services.GetRequiredService<IUserRepository>().GetById(id)
        ?? throw new InvalidOperationException("Пользователь исчез из хранилища.");

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
}

/// <summary>
/// FR-015, GET: профиль студента с группой (дословно демо-значения student01),
/// groupName по ТЕКУЩЕМУ состоянию групп, роль teacher допущена, анонимно —
/// детерминированный 401 (FR-022).
/// </summary>
public sealed class ProfileGetEndpointTests(ProfileGetApiFixture fixture) : IClassFixture<ProfileGetApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Get_StudentWithGroup_ReturnsExactDemoProfile()
    {
        // given: student01 авторизован и состоит в ИК-221 (демо-сид).
        var student = _factory.Services.GetRequiredService<IUserRepository>().GetByLogin("student01");
        Assert.NotNull(student);
        using var client = ProfileEndpointHarness.CreateSessionClient(_factory, student.Id, UserRoles.Student);

        // when: GET /me/profile под student01.
        using var response = await client.GetAsync(ProfileEndpointHarness.ProfileEndpoint);

        // then: 200 и ProfileDto с дословно кейсовыми значениями сид-данных.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = await ProfileEndpointHarness.ReadJsonAsync(response);
        Assert.Equal("student01", body.RootElement.GetProperty("login").GetString());
        Assert.Equal("student01@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("Иванов Иван Иванович 01", body.RootElement.GetProperty("fullName").GetString());
        Assert.Equal(UserRoles.Student, body.RootElement.GetProperty("role").GetString());
        Assert.Equal("ИК-221", body.RootElement.GetProperty("groupName").GetString());
    }

    [Fact]
    public async Task Get_TeacherWithoutGroup_GroupNameNull()
    {
        // given: авторизованный teacher (роль без группы).
        var teacher = _factory.Services.GetRequiredService<IUserRepository>().GetByLogin("teacher");
        Assert.NotNull(teacher);
        using var client = ProfileEndpointHarness.CreateSessionClient(_factory, teacher.Id, UserRoles.Teacher);

        // when: GET /me/profile.
        using var response = await client.GetAsync(ProfileEndpointHarness.ProfileEndpoint);

        // then: 200; groupName = null (teacher всегда без группы).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ProfileEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(UserRoles.Teacher, body.RootElement.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("groupName").ValueKind);
    }

    [Fact]
    public async Task Get_GroupRenamedAfterSeeding_ShowsCurrentName()
    {
        // given: студент в группе; состояние групп изменено ПОСЛЕ сида пользователя.
        var group = ProfileEndpointHarness.SeedGroup(_factory, "ИК-Гет-Переименование");
        var user = ProfileEndpointHarness.SeedUser(_factory, "profile-get-renamed", groupId: group.Id);
        ProfileEndpointHarness.RenameGroup(_factory, group, "ИК-Гет-Переименование (новое)");

        using var client = ProfileEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: GET /me/profile.
        using var response = await client.GetAsync(ProfileEndpointHarness.ProfileEndpoint);

        // then: 200; groupName вычислен по текущему имени группы.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ProfileEndpointHarness.ReadJsonAsync(response);
        Assert.Equal("ИК-Гет-Переименование (новое)", body.RootElement.GetProperty("groupName").GetString());
    }

    [Fact]
    public async Task Get_Anonymous_ReturnsDeterministicUnauthorizedEnvelope()
    {
        // when: GET /me/profile без access-cookie.
        using var client = ProfileEndpointHarness.CreateAnonymousClient(_factory);
        using var response = await client.GetAsync(ProfileEndpointHarness.ProfileEndpoint);

        // then: 401 {'message':'Не авторизован'} (FR-022, IF-001).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await ProfileEndpointHarness.MessageAsync(response));
    }
}

/// <summary>
/// FR-015, PUT: успешное редактирование (только fullName/email, поле login
/// игнорируется,groupName пересчитывается), границы длин, свой email без
/// конфликта, занятый email → 409, полевые ошибки → 400, битый JSON → 400
/// без errors.
/// </summary>
public sealed class ProfilePutEndpointTests(ProfilePutApiFixture fixture) : IClassFixture<ProfilePutApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Put_ValidValues_IgnoresLoginField_UpdatesOnlyNameAndEmail()
    {
        // given: студент с группой; состояние групп изменено ДО PUT; email свободен.
        var group = ProfileEndpointHarness.SeedGroup(_factory, "Группа Профиля Пут");
        var user = ProfileEndpointHarness.SeedUser(
            _factory,
            login: "profile-put-valid",
            email: "profile-put-valid@example.com",
            fullName: "Старое ФИО",
            groupId: group.Id);
        ProfileEndpointHarness.RenameGroup(_factory, group, "Группа Профиля Пут (переименована)");

        using var client = ProfileEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с валидными значениями и ЛИШНИМ полем login:'hacker'.
        using var response = await client.PutAsync(
            ProfileEndpointHarness.ProfileEndpoint,
            ProfileEndpointHarness.Json(
                """{"fullName":"Новое ФИО","email":"new-put@example.com","login":"hacker"}"""));

        // then: 200; ProfileDto с новыми значениями, прежними login/role и
        // пересчитанным groupName (поле login во входе игнорируется).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ProfileEndpointHarness.ReadJsonAsync(response);
        Assert.Equal("Новое ФИО", body.RootElement.GetProperty("fullName").GetString());
        Assert.Equal("new-put@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("profile-put-valid", body.RootElement.GetProperty("login").GetString());
        Assert.Equal(UserRoles.Student, body.RootElement.GetProperty("role").GetString());
        Assert.Equal("Группа Профиля Пут (переименована)", body.RootElement.GetProperty("groupName").GetString());

        // then: в хранилище обновлены только fullName/email.
        var stored = ProfileEndpointHarness.StoredUser(_factory, user.Id);
        Assert.Equal("profile-put-valid", stored.Login);
        Assert.Equal("Новое ФИО", stored.FullName);
        Assert.Equal("new-put@example.com", stored.Email);
        Assert.Equal(UserRoles.Student, stored.Role);
        Assert.Equal(group.Id, stored.GroupId);
    }

    [Fact]
    public async Task Put_SurroundingSpaces_StoresTrimmedValues()
    {
        // given: пользователь без группы.
        var user = ProfileEndpointHarness.SeedUser(_factory, "profile-put-trim");
        using var client = ProfileEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT со значениями в обрамляющих пробелах (правила — по триммированному).
        using var response = await client.PutAsync(
            ProfileEndpointHarness.ProfileEndpoint,
            ProfileEndpointHarness.Json("""{"fullName":"  Трим ФИО  ","email":" trim-put@example.com "}"""));

        // then: 200; в ответе и хранилище — триммированные значения.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ProfileEndpointHarness.ReadJsonAsync(response);
        Assert.Equal("Трим ФИО", body.RootElement.GetProperty("fullName").GetString());
        Assert.Equal("trim-put@example.com", body.RootElement.GetProperty("email").GetString());

        var stored = ProfileEndpointHarness.StoredUser(_factory, user.Id);
        Assert.Equal("Трим ФИО", stored.FullName);
        Assert.Equal("trim-put@example.com", stored.Email);
    }

    [Fact]
    public async Task Put_OwnEmail_NoConflict()
    {
        // given: пользователь с email X.
        var user = ProfileEndpointHarness.SeedUser(
            _factory,
            login: "profile-put-own",
            email: "own@example.com",
            fullName: "Своё ФИО");
        using var client = ProfileEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT без изменения email (совпадение с самим собой).
        using var response = await client.PutAsync(
            ProfileEndpointHarness.ProfileEndpoint,
            ProfileEndpointHarness.Json("""{"fullName":"Обновлённое ФИО","email":"own@example.com"}"""));

        // then: 200 — свой email не даёт 409; ФИО обновлено.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ProfileEndpointHarness.ReadJsonAsync(response);
        Assert.Equal("Обновлённое ФИО", body.RootElement.GetProperty("fullName").GetString());
        Assert.Equal("own@example.com", body.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Put_ForeignEmail_Returns409WithDictionaryText()
    {
        // given: два пользователя; второй занял целевой email.
        var user = ProfileEndpointHarness.SeedUser(
            _factory,
            login: "profile-put-conflict",
            email: "conflict-first@example.com",
            fullName: "Первый ФИО");
        ProfileEndpointHarness.SeedUser(
            _factory,
            login: "profile-put-foreign",
            email: "foreign@example.com",
            fullName: "Второй ФИО");
        using var client = ProfileEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с email другого пользователя (в другом регистре — ci-правило).
        using var response = await client.PutAsync(
            ProfileEndpointHarness.ProfileEndpoint,
            ProfileEndpointHarness.Json("""{"fullName":"Первый ФИО","email":"FOREIGN@example.com"}"""));

        // then: 409 с дословным текстом словаря; профиль не изменился.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Пользователь с таким email уже существует",
            await ProfileEndpointHarness.MessageAsync(response));

        var stored = ProfileEndpointHarness.StoredUser(_factory, user.Id);
        Assert.Equal("conflict-first@example.com", stored.Email);
        Assert.Equal("Первый ФИО", stored.FullName);
    }

    [Fact]
    public async Task Put_WhitespaceFullName_Returns400RequiredFullName()
    {
        // given: авторизованный пользователь.
        var user = ProfileEndpointHarness.SeedUser(_factory, "profile-put-space-name");
        using var client = ProfileEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с ФИО из одних пробелов и валидным email.
        using var response = await client.PutAsync(
            ProfileEndpointHarness.ProfileEndpoint,
            ProfileEndpointHarness.Json("""{"fullName":"  ","email":"a@b.ru"}"""));

        // then: 400 «Данные заполнены неверно»; errors.fullName=['Заполните поле'].
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await ProfileEndpointHarness.MessageAsync(response));
        var errors = await ProfileEndpointHarness.ErrorsAsync(response);
        Assert.Equal(["Заполните поле"], ProfileEndpointHarness.ErrorOf(errors, "fullName"));
    }

    [Fact]
    public async Task Put_InvalidEmailFormat_Returns400FormatError()
    {
        // given: авторизованный пользователь.
        var user = ProfileEndpointHarness.SeedUser(_factory, "profile-put-bad-email");
        using var client = ProfileEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с пробелом в локальной части email.
        using var response = await client.PutAsync(
            ProfileEndpointHarness.ProfileEndpoint,
            ProfileEndpointHarness.Json("""{"fullName":"Ф И О","email":"a b@example.com"}"""));

        // then: 400; errors.email=['Введите корректный email'].
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await ProfileEndpointHarness.MessageAsync(response));
        var errors = await ProfileEndpointHarness.ErrorsAsync(response);
        Assert.Equal(["Введите корректный email"], ProfileEndpointHarness.ErrorOf(errors, "email"));
    }

    [Fact]
    public async Task Put_BothFieldsInvalid_ReturnsAllErrorsAtOnce()
    {
        // given: авторизованный пользователь.
        var user = ProfileEndpointHarness.SeedUser(_factory, "profile-put-both-invalid");
        using var client = ProfileEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с пустым ФИО и отсутствующим email ({} после отказа от email).
        using var response = await client.PutAsync(
            ProfileEndpointHarness.ProfileEndpoint,
            ProfileEndpointHarness.Json("""{"fullName":""}"""));

        // then: 400; обе ошибки сразу (errors {fullName, email}).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ProfileEndpointHarness.ErrorsAsync(response);
        Assert.Equal(["Заполните поле"], ProfileEndpointHarness.ErrorOf(errors, "fullName"));
        Assert.Equal(["Заполните поле"], ProfileEndpointHarness.ErrorOf(errors, "email"));
    }

    [Fact]
    public async Task Put_EmailLengthBoundary_254Valid_255TooLong()
    {
        // given: авторизованный пользователь; границы — после трима, формат валиден.
        var user = ProfileEndpointHarness.SeedUser(_factory, "profile-put-email-boundary");
        using var client = ProfileEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с email длиной ровно 254 (242 + '@example.com').
        using var valid = await client.PutAsync(
            ProfileEndpointHarness.ProfileEndpoint,
            ProfileEndpointHarness.Json(
                $$"""{"fullName":"ФИО Граница","email":"{{new string('a', 242)}}@example.com"}"""));

        // then: 200.
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);

        // when: PUT с email длиной 255.
        using var invalid = await client.PutAsync(
            ProfileEndpointHarness.ProfileEndpoint,
            ProfileEndpointHarness.Json(
                $$"""{"fullName":"ФИО Граница","email":"{{new string('a', 243)}}@example.com"}"""));

        // then: 400; errors.email=['Email — не более 254 символов'].
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = await ProfileEndpointHarness.ErrorsAsync(invalid);
        Assert.Equal(
            ["Email — не более 254 символов"],
            ProfileEndpointHarness.ErrorOf(errors, "email"));
    }

    [Fact]
    public async Task Put_FullNameLengthBoundary_200Valid_201TooLong()
    {
        // given: авторизованный пользователь.
        var user = ProfileEndpointHarness.SeedUser(
            _factory,
            login: "profile-put-name-boundary",
            email: "name-boundary@example.com");
        using var client = ProfileEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с ФИО длиной ровно 200.
        using var valid = await client.PutAsync(
            ProfileEndpointHarness.ProfileEndpoint,
            ProfileEndpointHarness.Json(
                $$"""{"fullName":"{{new string('Ф', 200)}}","email":"name-boundary@example.com"}"""));

        // then: 200; ProfileDto с новым ФИО.
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var body = await ProfileEndpointHarness.ReadJsonAsync(valid);
        Assert.Equal(new string('Ф', 200), body.RootElement.GetProperty("fullName").GetString());

        // when: PUT с ФИО длиной 201.
        using var invalid = await client.PutAsync(
            ProfileEndpointHarness.ProfileEndpoint,
            ProfileEndpointHarness.Json(
                $$"""{"fullName":"{{new string('Ф', 201)}}","email":"name-boundary@example.com"}"""));

        // then: 400; errors.fullName=['ФИО — от 1 до 200 символов'].
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = await ProfileEndpointHarness.ErrorsAsync(invalid);
        Assert.Equal(
            ["ФИО — от 1 до 200 символов"],
            ProfileEndpointHarness.ErrorOf(errors, "fullName"));
    }

    [Fact]
    public async Task Put_MalformedJson_Returns400WithoutErrors()
    {
        // given: авторизованный пользователь.
        var user = ProfileEndpointHarness.SeedUser(_factory, "profile-put-broken-json");
        using var client = ProfileEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с синтаксически некорректным JSON.
        using var response = await client.PutAsync(
            ProfileEndpointHarness.ProfileEndpoint,
            ProfileEndpointHarness.Json("""{"fullName": """));

        // then: 400 «Данные заполнены неверно» БЕЗ errors (IF-001).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await ProfileEndpointHarness.MessageAsync(response));
        using var body = await ProfileEndpointHarness.ReadJsonAsync(response);
        Assert.False(
            body.RootElement.TryGetProperty("errors", out _),
            "Тело 400 битого JSON не должно содержать errors.");
    }
}
