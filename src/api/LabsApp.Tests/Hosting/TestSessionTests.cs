using System.Net;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Observability;
using LabsApp.Storage;
using LabsApp.Tests.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabsApp.Tests.Hosting;

/// <summary>
/// Unit-тесты хелпера TestSession (ADR-015, T-006): DI-сид пользователя отражается
/// в GET /auth/me (роль/группа), access-cookie предустановлена без POST /auth/login
/// (в журнале хоста нет запроса login), cookie-контейнер парсит Set-Cookie,
/// log-sink фильтруется по категориям.
/// </summary>
public sealed class TestSessionTests : IClassFixture<TestWebAppFactory>
{
    private const string MeEndpoint = "/api/v1/auth/me";
    private const string LogoutEndpoint = "/api/v1/auth/logout";

    private readonly TestWebAppFactory _factory;

    public TestSessionTests(TestWebAppFactory factory)
    {
        _factory = factory;
        _factory.LogSink.Clear();
    }

    // ------------------------------------------------------------------
    // AC «Сессия без HTTP-входа»: CreateStudent/CreateTeacher + GET /auth/me
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateStudent_Me_ReturnsSeededStudentMeDto_WithoutLoginCall()
    {
        using var session = TestSession.CreateStudent(_factory, new TestUserSeed
        {
            Login = "me-student",
            GroupName = "ИК-221-хелпер",
        });

        using var response = await session.Client.GetAsync(MeEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("me-student", body.RootElement.GetProperty("login").GetString());
        Assert.Equal(UserRoles.Student, body.RootElement.GetProperty("role").GetString());
        Assert.Equal("ИК-221-хелпер", body.RootElement.GetProperty("groupName").GetString());
        Assert.Equal("Тест Тестович Тестов", body.RootElement.GetProperty("fullName").GetString());

        // «Ни один /auth/login не вызван»: в записях журнала запросов хоста
        // (категория Api.Request) login-путь отсутствует — сессия создана сидом.
        Assert.DoesNotContain(
            _factory.LogSink.Snapshot(),
            record => IsRequestRecordFor(record, "/api/v1/auth/login"));
    }

    [Fact]
    public async Task CreateTeacher_Me_ReturnsTeacherRoleWithoutGroup()
    {
        using var session = TestSession.CreateTeacher(_factory, new TestUserSeed { Login = "me-teacher" });

        using var response = await session.Client.GetAsync(MeEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("me-teacher", body.RootElement.GetProperty("login").GetString());
        Assert.Equal(UserRoles.Teacher, body.RootElement.GetProperty("role").GetString());
        // FR-011: groupName null в JSON присутствует всегда (teacher — без группы).
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("groupName").ValueKind);
        Assert.DoesNotContain(
            _factory.LogSink.Snapshot(),
            record => IsRequestRecordFor(record, "/api/v1/auth/login"));
    }

    // ------------------------------------------------------------------
    // Лог-sink: фильтрация по категориям (запись Api.Request только о /auth/me)
    // ------------------------------------------------------------------

    [Fact]
    public async Task LogSink_CategoryFilter_RequestRecordOnlyForMadeEndpoint()
    {
        using var session = TestSession.CreateStudent(_factory, new TestUserSeed { Login = "sink-student" });

        using var response = await session.Client.GetAsync(MeEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = _factory.LogSink.Snapshot();
        // Позитивный фильтр: категория Api.Request содержит запись о me-запросе.
        var meRecord = Assert.Single(snapshot, record =>
            record.Category == ObservabilityMiddleware.RequestLogCategory
            && IsRequestRecordFor(record, MeEndpoint));
        Assert.Equal(LogLevel.Information, meRecord.Level);
        // Категорий безопасности (429/403 на этих сценариях нет) — тоже нет.
        Assert.DoesNotContain(
            snapshot,
            record => record.Category == ObservabilityMiddleware.SecurityLogCategory);
    }

    // ------------------------------------------------------------------
    // DI-сид: уникальные умолчания, пароль под реальным хэшем хоста
    // ------------------------------------------------------------------

    [Fact]
    public void Create_TwoStudentsOnSameHost_DefaultLoginsUnique_NoConflict()
    {
        using var first = TestSession.CreateStudent(_factory);
        using var second = TestSession.CreateStudent(_factory);

        Assert.NotEqual(first.User.Login, second.User.Login);
        Assert.Equal(UserRoles.Student, first.User.Role);
        Assert.Null(first.User.GroupId);
        Assert.NotNull(_factory.Services.GetRequiredService<IUserRepository>().GetByLogin(first.User.Login));
        Assert.NotNull(_factory.Services.GetRequiredService<IUserRepository>().GetByLogin(second.User.Login));
    }

    [Fact]
    public void SeedStudent_ExplicitFields_PasswordVerifiableByHostHasher()
    {
        var user = TestSession.SeedStudent(_factory, new TestUserSeed
        {
            Login = "seed-fields",
            Email = "seed-fields@example.com",
            FullName = "Сид Сидович Сидов",
            Password = "Test-Password1!",
        });

        Assert.Equal("seed-fields", user.Login);
        Assert.Equal("seed-fields@example.com", user.Email);
        Assert.Equal("Сид Сидович Сидов", user.FullName);
        Assert.True(
            _factory.Services.GetRequiredService<IPasswordHasher>()
                .Verify("Test-Password1!", user.PasswordHash, KdfCallers.Seed));
    }

    // ------------------------------------------------------------------
    // Access-cookie: предустановлена в заголовках клиента и в контейнере
    // ------------------------------------------------------------------

    [Fact]
    public void CreateSession_AccessCookie_PreinstalledInHeaderAndContainer()
    {
        using var session = TestSession.CreateStudent(_factory, new TestUserSeed { Login = "cookie-student" });

        var expected = $"{AuthCoreDefaults.AccessTokenCookieName}={session.Cookies.GetValue(AuthCoreDefaults.AccessTokenCookieName)}";
        Assert.True(session.Cookies.Contains(AuthCoreDefaults.AccessTokenCookieName));
        Assert.Contains(
            expected,
            session.Client.DefaultRequestHeaders.TryGetValues("Cookie", out var cookies)
                ? string.Join("; ", cookies)
                : string.Empty,
            StringComparison.Ordinal);
        // Минт реальным компонентом: токен валидируется ITokenService того же хоста.
        Assert.NotNull(_factory.Services.GetRequiredService<ITokenService>()
            .ValidateAccessToken(session.Cookies.GetValue(AuthCoreDefaults.AccessTokenCookieName)));
    }

    // ------------------------------------------------------------------
    // Cookie-контейнер: парсинг Set-Cookie ответа (logout сбрасывает обе cookie)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Session_LogoutClearCookies_ContainerCapturesSetCookieDeletion()
    {
        using var session = TestSession.CreateStudent(_factory, new TestUserSeed { Login = "logout-student" });
        Assert.Equal(1, session.Cookies.Count);

        using var response = await session.Client.PostAsync(LogoutEndpoint, content: null);
        session.Cookies.CaptureFrom(response);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        // Обе Set-Cookie с Max-Age=0 распознаны как удаление — контейнер пуст.
        Assert.Equal(0, session.Cookies.Count);
        Assert.False(session.Cookies.Contains(AuthCoreDefaults.AccessTokenCookieName));
        Assert.False(session.Cookies.Contains(AuthCoreDefaults.RefreshTokenCookieName));
    }

    // ------------------------------------------------------------------
    // Доменные границы хелпера
    // ------------------------------------------------------------------

    [Fact]
    public void CreateTeacher_WithGroupName_Throws()
    {
        Assert.Throws<ArgumentException>(() => TestSession.CreateTeacher(
            _factory,
            new TestUserSeed { GroupName = "ИК-221-хелпер" }));
        Assert.Throws<ArgumentException>(() => TestSession.SeedTeacher(
            _factory,
            new TestUserSeed { GroupName = "ИК-221-хелпер" }));
    }

    [Fact]
    public void SeedStudent_ExistingGroup_ReusedWithoutDuplicate()
    {
        var groupId = TestSession.SeedStudent(
            _factory,
            new TestUserSeed { Login = "group-reuse-1", GroupName = "ИК-222-хелпер" }).GroupId!.Value;
        var secondId = TestSession.SeedStudent(
            _factory,
            new TestUserSeed { Login = "group-reuse-2", GroupName = "ИК-222-хелпер" }).GroupId!.Value;

        Assert.Equal(groupId, secondId);
        // Повторный сид не создал дубликат группы (ci-поиск переиспользует запись).
        Assert.Equal(1, _factory.Services.GetRequiredService<IGroupRepository>().GetAll()
            .Count(group => group.Name == "ИК-222-хелпер"));
    }

    private static bool IsRequestRecordFor(TestLogRecord record, string path) =>
        record.Category == ObservabilityMiddleware.RequestLogCategory
        && record.Message.Contains(path, StringComparison.Ordinal);

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }
}
