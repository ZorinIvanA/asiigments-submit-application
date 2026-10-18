using System.Net;
using System.Net.Http.Json;
using System.Text;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Observability;
using LabsApp.Storage;
using LabsApp.Tests.Hosting;
using LabsApp.Tests.Recovery;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabsApp.Tests.Observability;

// ============================================================================
// Эндпойнт-тесты журнала событий безопасности (IF-016, аменда CR-002/ADR-034,
// T-125): наличие/отсутствие записей «Api.Security» по операциям — смена пароля
// (PUT /me/password, AC «Смена пароля логируется»), идемпотентный logout без
// cookie (AC «Идемпотентный logout молчит»), logout с живым refresh (запись
// reason=logout), единая категория Warning-записей (AC «Категория едина») и
// маркерная проверка NFR-006 — пароли/токены не встречаются в записях.
// Хост — TestWebAppFactory (Development, итерации KDF 1000, без демо-набора);
// log-sink — LogSink фикстуры; изоляция — LogSink.Clear() перед окном операций.
// ============================================================================

/// <summary>Фикстура хоста: без демо-набора, тестовые итерации KDF (1000).</summary>
public sealed class SecurityEventLoggingApiFixture : IDisposable
{
    public TestWebAppFactory Factory { get; } = new(
        null,
        new Dictionary<string, string?>
        {
            [SeedOptions.DemoDataVariable] = "false",
            [AuthOptions.Pbkdf2IterationsVariable] = "1000",
        });

    public void Dispose() => Factory.Dispose();
}

/// <summary>Харнес: анонимные клиенты, DI-сид пользователей/refresh-токенов, выборка записей «Api.Security».</summary>
internal static class SecurityEventLoggingHarness
{
    public const string PasswordEndpoint = "/api/v1/me/password";
    public const string LogoutEndpoint = "/api/v1/auth/logout";
    public const string LoginEndpoint = "/api/v1/auth/login";

    /// <summary>Пароль DI-сид-пользователей до смены (правила FR-006 соблюдены).</summary>
    public const string TestUserPassword = "student123!";

    /// <summary>Валидный новый пароль смены.</summary>
    public const string NewPassword = "NewPass1!";

    public static HttpClient CreateAnonymousClient(TestWebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>DI-сид пользователя с РЕАЛЬНЫМ PBKDF2-хэшем (метка seed).</summary>
    public static User SeedUser(TestWebAppFactory factory, string login, string password = TestUserPassword)
    {
        var passwordHash = factory.Services
            .GetRequiredService<IPasswordHasher>()
            .Hash(password, KdfCallers.Seed);
        var repository = factory.Services.GetRequiredService<IUserRepository>();
        repository.Add(new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = $"{login}@example.com",
            PasswordHash = passwordHash,
            FullName = "Тест Тестович Тестов",
            Role = UserRoles.Student,
            GroupId = null,
            CreatedAt = DateTime.UtcNow,
        });
        return repository.GetByLogin(login)
            ?? throw new InvalidOperationException($"Пользователь {login} не сохранился при DI-сиде.");
    }

    /// <summary>
    /// given «живой refresh-токен»: ITokenService.CreateRefreshToken + запись в
    /// ISecurityTokenRepository; возвращает ОТКРЫТОЕ значение (для cookie/маркера).
    /// </summary>
    public static string SeedRefreshToken(TestWebAppFactory factory, Guid userId)
    {
        var grant = factory.Services
            .GetRequiredService<ITokenService>()
            .CreateRefreshToken(userId);

        factory.Services.GetRequiredService<ISecurityTokenRepository>().Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = grant.TokenHash,
            ExpiresAt = grant.ExpiresAt,
            CreatedAt = DateTime.UtcNow,
        });

        return grant.Value;
    }

    /// <summary>Клиент с ЯВНЫМ заголовком Cookie (access и/или refresh по переданным значениям).</summary>
    public static HttpClient CreateCookieClient(TestWebAppFactory factory, string cookie)
    {
        var client = CreateAnonymousClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    public static string AccessCookieOf(TestWebAppFactory factory, Guid userId, string role = UserRoles.Student) =>
        $"{AuthCoreDefaults.AccessTokenCookieName}={factory.Services.GetRequiredService<ITokenService>().IssueAccessToken(userId, role)}";

    /// <summary>Значения заголовков Set-Cookie ответа (пустой массив — заголовка нет).</summary>
    public static string[] SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];

    /// <summary>
    /// Значение access-JWT из Set-Cookie (пара «имя=значение» до первого «;») —
    /// для разбора клеймов выпущенного токена (AC «Минт login-claim»).
    /// </summary>
    public static string AccessJwtFromSetCookie(HttpResponseMessage response)
    {
        var header = Assert.Single(SetCookies(response), cookie =>
            cookie.StartsWith($"{AuthCoreDefaults.AccessTokenCookieName}=", StringComparison.Ordinal));
        return header[(header.IndexOf('=') + 1)..].Split(';')[0];
    }

    public static Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string current, string password) =>
        client.PutAsync(
            PasswordEndpoint,
            new StringContent(
                $$"""{"currentPassword":"{{current}}","password":"{{password}}","confirmPassword":"{{password}}"}""",
                Encoding.UTF8,
                "application/json"));

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginEndpoint, new { login, password });

    /// <summary>Записи «Api.Security» текущего sink-снимка.</summary>
    public static IReadOnlyList<TestLogRecord> SecurityRecords(TestWebAppFactory factory) =>
        [.. factory.LogSink.Snapshot().Where(record => record.Category == SecurityEventLogger.LogCategory)];

    /// <summary>Сериализованная запись (сообщение + шаблон + состояние) — поверхность маркерной проверки NFR-006.</summary>
    public static string SerializeForMarkerCheck(TestLogRecord record) =>
        string.Join(
            "|",
            new[]
            {
                record.Category,
                record.Level.ToString(),
                record.Message,
                record.MessageTemplate ?? string.Empty,
            }.Concat(record.State.Select(pair => $"{pair.Key}={pair.Value}")));
}

/// <summary>
/// AC «Смена пароля логируется»: успех PUT /me/password (204) — РОВНО две записи
/// «Api.Security» (факт смены + отзыв refresh с reason=password_change); отказ
/// (неверный currentPassword, 400) — ни одной записи. Маркерная проверка NFR-006:
/// пароли и значения refresh-токенов не встречаются ни в одной записи.
/// </summary>
public sealed class PasswordChangeSecurityLogTests(SecurityEventLoggingApiFixture fixture)
    : IClassFixture<SecurityEventLoggingApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Put_Success_LogsPasswordChangeAndRevocationFacts_WithoutSecrets()
    {
        // given: пользователь; refresh-токен текущей сессии (cookie) и «второго устройства».
        var user = SecurityEventLoggingHarness.SeedUser(_factory, "sec-log-pwd");
        var currentRefresh = SecurityEventLoggingHarness.SeedRefreshToken(_factory, user.Id);
        var otherRefresh = SecurityEventLoggingHarness.SeedRefreshToken(_factory, user.Id);
        using var client = SecurityEventLoggingHarness.CreateCookieClient(
            _factory,
            $"{SecurityEventLoggingHarness.AccessCookieOf(_factory, user.Id)}; " +
            $"{AuthCoreDefaults.RefreshTokenCookieName}={currentRefresh}");

        // given: чистый log-sink вокруг измеряемой операции.
        _factory.LogSink.Clear();

        // when: успешная смена пароля (верный текущий, валидный новый) — 204.
        using var response = await SecurityEventLoggingHarness.ChangePasswordAsync(
            client, SecurityEventLoggingHarness.TestUserPassword, SecurityEventLoggingHarness.NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // then: РОВНО две записи «Api.Security», обе Warning — факт смены и отзыв.
        var records = SecurityEventLoggingHarness.SecurityRecords(_factory);
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal(LogLevel.Warning, record.Level));
        Assert.Single(records, record =>
            record.State.TryGetValue("Fact", out var fact) && fact as string == "Смена пароля");
        Assert.Single(records, record =>
            record.State.TryGetValue("Reason", out var reason) && reason as string == SecurityEventReasons.PasswordChange);
        Assert.All(records, record => Assert.False(
            string.IsNullOrWhiteSpace((string)record.State["TraceId"]!),
            "Запись события безопасности обязана нести traceId."));

        // then (NFR-006): маркеры секретов не встречаются НИ В ОДНОЙ записи sink
        // (вне «EmailDev» — категорий с секретами в этом потоке нет вовсе):
        // ни старый/новый пароль, ни значения refresh-токенов.
        foreach (var record in _factory.LogSink.Snapshot())
        {
            var serialized = SecurityEventLoggingHarness.SerializeForMarkerCheck(record);
            Assert.DoesNotContain(SecurityEventLoggingHarness.TestUserPassword, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(SecurityEventLoggingHarness.NewPassword, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(currentRefresh, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(otherRefresh, serialized, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Put_WrongCurrentPassword_WritesNoSecurityRecords()
    {
        // given: пользователь; сессия минтована; чистый log-sink.
        var user = SecurityEventLoggingHarness.SeedUser(_factory, "sec-log-pwd-bad");
        using var client = SecurityEventLoggingHarness.CreateCookieClient(
            _factory, SecurityEventLoggingHarness.AccessCookieOf(_factory, user.Id));
        _factory.LogSink.Clear();

        // when: НЕверный currentPassword → 400 (смена не состоялась).
        using var response = await SecurityEventLoggingHarness.ChangePasswordAsync(client, "wrong", "NewPass1!");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // then: записей событий безопасности нет — фиксируется только факт операции.
        Assert.Empty(SecurityEventLoggingHarness.SecurityRecords(_factory));
    }
}

/// <summary>
/// AC «Идемпотентный logout молчит»: POST /auth/logout без cookie — 204 БЕЗ записей
/// об отзыве; logout с живым refresh — одна запись reason=logout без значения токена.
/// </summary>
public sealed class LogoutSecurityLogTests(SecurityEventLoggingApiFixture fixture)
    : IClassFixture<SecurityEventLoggingApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Logout_WithLiveRefresh_LogsRevocationWithLogoutReason_WithoutTokenValue()
    {
        // given: пользователь с живым refresh-токеном; только refresh-cookie
        // (валидный access не требуется — FR-010).
        var user = SecurityEventLoggingHarness.SeedUser(_factory, "sec-log-out");
        var refresh = SecurityEventLoggingHarness.SeedRefreshToken(_factory, user.Id);
        using var client = SecurityEventLoggingHarness.CreateCookieClient(
            _factory, $"{AuthCoreDefaults.RefreshTokenCookieName}={refresh}");

        // given: чистый log-sink; when: POST /auth/logout.
        _factory.LogSink.Clear();
        using var logout = await client.PostAsync(SecurityEventLoggingHarness.LogoutEndpoint, content: null);

        // then: 204; РОВНО одна запись «Api.Security» (Warning) с reason=logout.
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var records = SecurityEventLoggingHarness.SecurityRecords(_factory);
        var record = Assert.Single(records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(SecurityEventReasons.Logout, record.State["Reason"]);
        Assert.False(string.IsNullOrWhiteSpace((string)record.State["TraceId"]!));

        // then (NFR-006): значение отозванного токена в записи отсутствует.
        Assert.DoesNotContain(
            refresh,
            SecurityEventLoggingHarness.SerializeForMarkerCheck(record),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_WithoutCookies_IsSilent_NoRevocationRecords()
    {
        // given: запрос без cookie access_token и refresh_token; чистый log-sink.
        using var client = SecurityEventLoggingHarness.CreateAnonymousClient(_factory);
        _factory.LogSink.Clear();

        // when: два идемпотентных logout подряд.
        using var first = await client.PostAsync(SecurityEventLoggingHarness.LogoutEndpoint, content: null);
        using var second = await client.PostAsync(SecurityEventLoggingHarness.LogoutEndpoint, content: null);

        // then: оба 204; записей об отзыве НЕТ — фактического отзыва не было
        // (AC «Идемпотентный logout молчит», IF-016 «запись только при факте»).
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Empty(SecurityEventLoggingHarness.SecurityRecords(_factory));
    }
}

/// <summary>
/// AC «Категория едина»: Warning-записи обоих продюсеров (ObservabilityMiddleware
/// — 429, ISecurityEventLogger — смена пароля) в sink имеют одну категорию
/// «Api.Security»; отдельных категорий «Security» не существует.
/// </summary>
public sealed class SecurityCategoryUnificationTests(SecurityEventLoggingApiFixture fixture)
    : IClassFixture<SecurityEventLoggingApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task AllWarningRecords_MiddlewareAndLogger_UseUnifiedApiSecurityCategory()
    {
        // given: уникальный IP (окно лимитера не пересекается с другими тестами).
        var user = SecurityEventLoggingHarness.SeedUser(_factory, "sec-log-uniform");
        using var passwordClient = SecurityEventLoggingHarness.CreateCookieClient(
            _factory, SecurityEventLoggingHarness.AccessCookieOf(_factory, user.Id));
        using var loginClient = SecurityEventLoggingHarness.CreateAnonymousClient(_factory);
        TestWebAppFactory.SetClientIp(loginClient, "192.0.2.171");

        // given: чистый log-sink; 5 неудачных входов исчерпывают окно login (5/60с).
        _factory.LogSink.Clear();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await SecurityEventLoggingHarness.LoginAsync(
                loginClient, "sec-log-uniform", "wrong-password");
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        // when: 6-я попытка → 429 (Warning от ObservabilityMiddleware) и успешная
        // смена пароля → две Warning-записи от ISecurityEventLogger.
        using var rateLimited = await SecurityEventLoggingHarness.LoginAsync(
            loginClient, "sec-log-uniform", "wrong-password");
        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimited.StatusCode);

        using var change = await SecurityEventLoggingHarness.ChangePasswordAsync(
            passwordClient, SecurityEventLoggingHarness.TestUserPassword, SecurityEventLoggingHarness.NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        // then: ВСЕ Warning-записи sink — категория «Api.Security» (единая
        // константа); среди них есть записи обоих продюсеров (429 и отзыв).
        var warnings = _factory.LogSink.Snapshot()
            .Where(record => record.Level == LogLevel.Warning)
            .ToList();
        Assert.Equal(3, warnings.Count);
        Assert.All(warnings, record =>
            Assert.Equal(ObservabilityMiddleware.SecurityLogCategory, record.Category));
        Assert.Single(warnings, record => record.State.ContainsKey("Status"));
        Assert.Single(warnings, record => record.State.ContainsKey("Reason"));
        Assert.Single(warnings, record => record.State.ContainsKey("Fact"));
    }
}

/// <summary>
/// Канал subject (аменда CR-001/ADR-044, IF-016): access-JWT, выпущенный с
/// непустым login (трёхаргументная IssueAccessToken — прод-форма refresh/
/// IssueSession), доставляет claim «login» до principal — записи «Api.Security»
/// аутентифицированного события (смена пароля + отзыв refresh) несут
/// subject=&lt;login&gt;. Токен харнеса БЕЗ claim (толерантность IF-003) — записи
/// без subject, поведение идентично состоянию до аменды; reset-password —
/// анонимная операция, записи без subject при любом состоянии канала.
/// </summary>
public sealed class SubjectChannelSecurityLogTests(SecurityEventLoggingApiFixture fixture)
    : IClassFixture<SecurityEventLoggingApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task PasswordChange_WithLoginClaim_SecurityRecordsCarrySubject()
    {
        // given: пользователь; access минтован ПРОД-формой (с login), refresh
        // текущей сессии — в cookie (ветка ISS-002).
        var user = SecurityEventLoggingHarness.SeedUser(_factory, "subject-pwd");
        var currentRefresh = SecurityEventLoggingHarness.SeedRefreshToken(_factory, user.Id);
        var access = _factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(user.Id, user.Role, user.Login);
        using var client = SecurityEventLoggingHarness.CreateCookieClient(
            _factory,
            $"{AuthCoreDefaults.AccessTokenCookieName}={access}; " +
            $"{AuthCoreDefaults.RefreshTokenCookieName}={currentRefresh}");

        // when: успешная смена пароля — 204.
        _factory.LogSink.Clear();
        using var response = await SecurityEventLoggingHarness.ChangePasswordAsync(
            client, SecurityEventLoggingHarness.TestUserPassword, SecurityEventLoggingHarness.NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // then: РОВНО две записи «Api.Security» (факт смены + отзыв refresh),
        // обе несут subject=<login>; секретов в записях нет (NFR-006).
        var records = SecurityEventLoggingHarness.SecurityRecords(_factory);
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal(user.Login, record.State["Subject"]));

        foreach (var record in _factory.LogSink.Snapshot())
        {
            var serialized = SecurityEventLoggingHarness.SerializeForMarkerCheck(record);
            Assert.DoesNotContain(
                SecurityEventLoggingHarness.TestUserPassword, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(
                SecurityEventLoggingHarness.NewPassword, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(currentRefresh, serialized, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PasswordChange_WithoutLoginClaim_SecurityRecordsHaveNoSubject()
    {
        // given: access минтован ДВУХАРГУМЕНТНОЙ формой (тестовый харнес, IF-003) —
        // токен валиден, principal без claim «login».
        var user = SecurityEventLoggingHarness.SeedUser(_factory, "subject-pwd-null");
        using var client = SecurityEventLoggingHarness.CreateCookieClient(
            _factory, SecurityEventLoggingHarness.AccessCookieOf(_factory, user.Id));

        // when: успешная смена пароля — 204.
        _factory.LogSink.Clear();
        using var response = await SecurityEventLoggingHarness.ChangePasswordAsync(
            client, SecurityEventLoggingHarness.TestUserPassword, SecurityEventLoggingHarness.NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // then: записи есть (факт и отзыв), но subject опущен — идентично
        // состоянию до аменды.
        var records = SecurityEventLoggingHarness.SecurityRecords(_factory);
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.False(record.State.ContainsKey("Subject")));
    }

    [Fact]
    public async Task ResetPassword_SecurityRecordsHaveNoSubject_EvenWithLiveSubjectChannel()
    {
        // given: пользователь с живым reset-токеном; сброс выполняется АНОНИМНО —
        // операция не аутентифицирована (граница ADR-044), субъект — traceId.
        var user = RecoveryEndpointHarness.SeedUser(_factory, "subject-reset");
        var resetToken = RecoveryEndpointHarness.SeedResetToken(_factory, user.Id);
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);

        // when: успешный сброс пароля — 204.
        _factory.LogSink.Clear();
        using var response = await RecoveryEndpointHarness.ResetAsync(
            client, resetToken, RecoveryEndpointHarness.NewPassword, RecoveryEndpointHarness.NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // then: РОВНО две записи «Api.Security» (факт сброса + отзыв refresh),
        // обе БЕЗ subject — несмотря на живой канал (claim минтится только
        // аутентифицированным выпускам).
        var records = SecurityEventLoggingHarness.SecurityRecords(_factory);
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.False(record.State.ContainsKey("Subject")));
    }

    [Fact]
    public async Task LoginThroughHttp_Forbidden403_SecurityRecordCarriesSubjectFromRealSession()
    {
        // given: DI-сид student; сессия получается РЕАЛЬНЫМ POST /auth/login
        // (прод-путь IssueSession → JWT с claim «login» → Set-Cookie), cookie
        // переносит сценарий — вручную, как во всех сквозных сценариях.
        var user = SecurityEventLoggingHarness.SeedUser(_factory, "subject-login-403");
        using var client = SecurityEventLoggingHarness.CreateAnonymousClient(_factory);
        var container = new TestCookieContainer();

        _factory.LogSink.Clear();
        using var login = await SecurityEventLoggingHarness.LoginAsync(
            client, user.Login, SecurityEventLoggingHarness.TestUserPassword);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        container.CaptureFrom(login);
        Assert.True(
            container.Contains(AuthCoreDefaults.AccessTokenCookieName),
            "Успешный login обязан выставить access_token (прод-форма IssueSession).");

        // when: аутентифицированный запрос, завершающийся 403 (student → teacher-эндпойнт).
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/labs");
        container.ApplyTo(request);
        using var forbidden = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // then: РОВНО одна запись «Api.Security» (сам login 200 ничего не пишет) —
        // об отказе 403, и она несёт subject=<login> из сессии, полученной входом.
        var records = SecurityEventLoggingHarness.SecurityRecords(_factory);
        var record = Assert.Single(records);
        Assert.Equal(403, (int)record.State["Status"]!);
        Assert.Equal(user.Login, record.State["Subject"]);
        Assert.False(string.IsNullOrWhiteSpace((string)record.State["TraceId"]!));
    }

    [Fact]
    public async Task Logout_WithValidAccessAndLiveRefresh_SecurityRecordCarriesSubject()
    {
        // given: пользователь; живой refresh и ВАЛИДНЫЙ access с claim «login»
        // (трёхаргументная прод-форма) в cookie.
        var user = SecurityEventLoggingHarness.SeedUser(_factory, "subject-logout");
        var refresh = SecurityEventLoggingHarness.SeedRefreshToken(_factory, user.Id);
        var access = _factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(user.Id, user.Role, user.Login);
        using var client = SecurityEventLoggingHarness.CreateCookieClient(
            _factory,
            $"{AuthCoreDefaults.AccessTokenCookieName}={access}; " +
            $"{AuthCoreDefaults.RefreshTokenCookieName}={refresh}");

        // when: logout — 204 с фактическим отзывом живого refresh.
        _factory.LogSink.Clear();
        using var logout = await client.PostAsync(SecurityEventLoggingHarness.LogoutEndpoint, content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // then: РОВНО одна запись «Api.Security» (Warning, reason=logout),
        // несущая subject=<login>; значение отозванного токена в записи
        // отсутствует (NFR-006).
        var record = Assert.Single(SecurityEventLoggingHarness.SecurityRecords(_factory));
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(SecurityEventReasons.Logout, record.State["Reason"]);
        Assert.Equal(user.Login, record.State["Subject"]);
        Assert.False(string.IsNullOrWhiteSpace((string)record.State["TraceId"]!));
        Assert.DoesNotContain(
            refresh,
            SecurityEventLoggingHarness.SerializeForMarkerCheck(record),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_WithLiveRefreshWithoutAccess_SecurityRecordHasNoSubject()
    {
        // given: живой refresh в cookie, access ОТСУТСТВУЕТ — операция анонимна
        // (граница ADR-044: актор идентифицируется traceId + владением refresh).
        var user = SecurityEventLoggingHarness.SeedUser(_factory, "subject-logout-anon");
        var refresh = SecurityEventLoggingHarness.SeedRefreshToken(_factory, user.Id);
        using var client = SecurityEventLoggingHarness.CreateCookieClient(
            _factory, $"{AuthCoreDefaults.RefreshTokenCookieName}={refresh}");

        // when: logout — 204 с фактическим отзывом.
        _factory.LogSink.Clear();
        using var logout = await client.PostAsync(SecurityEventLoggingHarness.LogoutEndpoint, content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // then: запись есть (факт отзыва, reason=logout), но БЕЗ subject —
        // claim предъявлен не был; значение токена не логируется (NFR-006).
        var record = Assert.Single(SecurityEventLoggingHarness.SecurityRecords(_factory));
        Assert.Equal(SecurityEventReasons.Logout, record.State["Reason"]);
        Assert.False(record.State.ContainsKey("Subject"));
        Assert.DoesNotContain(
            refresh,
            SecurityEventLoggingHarness.SerializeForMarkerCheck(record),
            StringComparison.Ordinal);
    }
}

/// <summary>
/// AC «Минт login-claim» (IF-003, аменда CR-001/ADR-044): все три прод-call-сайта
/// выпуска access-JWT — IssueSession из register, IssueSession из login и refresh —
/// минтят claim «login» со значением учётки; проверка РАЗБОРОМ JWT из Set-Cookie.
/// </summary>
public sealed class LoginClaimMintingEndpointTests(SecurityEventLoggingApiFixture fixture)
    : IClassFixture<SecurityEventLoggingApiFixture>
{
    private static readonly System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler JwtHandler = new();

    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Register_IssuesAccessJwtWithLoginClaim()
    {
        using var client = SecurityEventLoggingHarness.CreateAnonymousClient(_factory);

        // when: register — 201 с access-cookie (прод-форма IssueSession с login).
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                fullName = "Минт Клейм",
                login = "mint-register",
                email = "mint-register@example.com",
                password = SecurityEventLoggingHarness.TestUserPassword,
                repeatPassword = SecurityEventLoggingHarness.TestUserPassword,
            });

        // then: access-JWT из Set-Cookie содержит ровно один claim «login».
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AssertSingleLoginClaim(
            SecurityEventLoggingHarness.AccessJwtFromSetCookie(response), "mint-register");
    }

    [Fact]
    public async Task Login_IssuesAccessJwtWithLoginClaim()
    {
        var user = SecurityEventLoggingHarness.SeedUser(_factory, "mint-login");
        using var client = SecurityEventLoggingHarness.CreateAnonymousClient(_factory);

        // when: login — 200 с access-cookie.
        using var response = await SecurityEventLoggingHarness.LoginAsync(
            client, user.Login, SecurityEventLoggingHarness.TestUserPassword);

        // then: claim «login» = логин учётки.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSingleLoginClaim(
            SecurityEventLoggingHarness.AccessJwtFromSetCookie(response), user.Login);
    }

    [Fact]
    public async Task Refresh_IssuesNewAccessJwtWithLoginClaim()
    {
        var user = SecurityEventLoggingHarness.SeedUser(_factory, "mint-refresh");
        var refresh = SecurityEventLoggingHarness.SeedRefreshToken(_factory, user.Id);
        using var client = SecurityEventLoggingHarness.CreateCookieClient(
            _factory, $"{AuthCoreDefaults.RefreshTokenCookieName}={refresh}");

        // when: refresh — 204 с НОВОЙ access-cookie (refresh не ротируется).
        using var response = await client.PostAsync("/api/v1/auth/refresh", content: null);

        // then: перевыпущенный access-JWT несёт claim «login».
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertSingleLoginClaim(
            SecurityEventLoggingHarness.AccessJwtFromSetCookie(response), user.Login);
    }

    /// <summary>claim «login» — ровно один, со значением логина учётки.</summary>
    private static void AssertSingleLoginClaim(string jwt, string expectedLogin)
    {
        var token = JwtHandler.ReadJwtToken(jwt);
        Assert.Equal(
            1,
            token.Claims.Count(claim => claim.Type == AuthCoreDefaults.LoginClaimType));
        Assert.Equal(expectedLogin, (string?)token.Payload[AuthCoreDefaults.LoginClaimType]);
    }
}
