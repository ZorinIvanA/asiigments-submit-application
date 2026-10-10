using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using LabsApp.Tests.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ValidationTexts = LabsApp.Domain.Validation.ErrorTexts;

namespace LabsApp.Tests.Profile;

// ============================================================================
// Эндпойнт-тесты MePasswordController (C-011, IF-013; FR-016 + Δkdf-гейт
// FR-027 по веткам 0/1/2 + арбитраж ISS-002). Отдельный файл — параллельность
// с ProfileEndpointTests (T-011). Сессии — DI-минт access-JWT через
// ITokenService хоста (ADR-015); refresh-токены «устройств» минтся
// ITokenService.CreateRefreshToken и кладутся в ISecurityTokenRepository
// (хранится только TokenHash, IF-003); cookie передаются заголовком Cookie
// ЯВНО на каждый запрос. Пользователи сидуются DI с РЕАЛЬНЫМ PBKDF2-хэшем
// (IPasswordHasher хоста, метка seed) — ветки «верный/неверный/дословный
// текущий пароль» сверяются с хранимым хэшем. Гейты Δkdf — снимки
// IKdfCounter.Snapshot() до/после измеряемого PUT (FR-027).
// ============================================================================

/// <summary>Фикстура хоста: без демо-набора, тестовые итерации KDF (1000).</summary>
public sealed class MePasswordApiFixture : IDisposable
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

/// <summary>
/// Харнес эндпойнта /me/password: сессии (минт access-cookie), DI-сид
/// пользователей с реальным хэшем и refresh-токенов, снимки KDF-счётчика,
/// чтение JSON-тел и конверта ошибок IF-001.
/// </summary>
internal static class MePasswordEndpointHarness
{
    public const string PasswordEndpoint = "/api/v1/me/password";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RefreshEndpoint = "/api/v1/auth/refresh";

    /// <summary>Пароль DI-сид-пользователей до смены (правила FR-006 соблюдены).</summary>
    public const string TestUserPassword = "student123!";

    /// <summary>Валидный новый пароль смены (буквально из when кейсов FR-016).</summary>
    public const string NewPassword = "NewPass1!";

    public static HttpClient CreateAnonymousClient(TestWebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// Клиент с ЯВНЫМ заголовком Cookie access_token={минтованный access-JWT};
    /// <paramref name="refreshTokenValue"/> — значение refresh-cookie текущей
    /// сессии (передаётся тем же заголовком, кейс ISS-002).
    /// </summary>
    public static HttpClient CreateSessionClient(
        TestWebAppFactory factory,
        Guid userId,
        string role,
        string? refreshTokenValue = null)
    {
        var jwt = factory.Services
            .GetRequiredService<ITokenService>()
            .IssueAccessToken(userId, role);

        var cookie = $"{AuthCoreDefaults.AccessTokenCookieName}={jwt}";
        if (refreshTokenValue is not null)
        {
            cookie += $"; {AuthCoreDefaults.RefreshTokenCookieName}={refreshTokenValue}";
        }

        var client = CreateAnonymousClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    /// <summary>
    /// DI-сид пользователя с РЕАЛЬНЫМ PBKDF2-хэшем пароля (метка seed — Δkdf
    /// считается снимками ПОСЛЕ сида, IF-002: каждая деривация считается).
    /// </summary>
    public static User SeedUser(
        TestWebAppFactory factory,
        string login,
        string? email = null,
        string? fullName = null,
        string role = UserRoles.Student,
        string password = TestUserPassword)
    {
        var passwordHash = factory.Services
            .GetRequiredService<IPasswordHasher>()
            .Hash(password, KdfCallers.Seed);
        var repository = factory.Services.GetRequiredService<IUserRepository>();
        repository.Add(new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email ?? $"{login}@example.com",
            PasswordHash = passwordHash,
            FullName = fullName ?? "Тест Тестович Тестов",
            Role = role,
            GroupId = null,
            CreatedAt = DateTime.UtcNow,
        });
        return repository.GetByLogin(login)
            ?? throw new InvalidOperationException($"Пользователь {login} не сохранился при DI-сиде.");
    }

    /// <summary>
    /// given «действующий refresh-токен устройства»: ITokenService.CreateRefreshToken
    /// + запись в ISecurityTokenRepository; возвращает ОТКРЫТОЕ значение токена.
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

    /// <summary>Пользователь по uuid из хранилища тестового хоста (копия-снимок).</summary>
    public static User StoredUser(TestWebAppFactory factory, Guid id) =>
        factory.Services.GetRequiredService<IUserRepository>().GetById(id)
        ?? throw new InvalidOperationException("Пользователь исчез из хранилища.");

    /// <summary>
    /// ЖИВАЯ запись refresh-токена по ОТКРЫТОМУ значению (хэш считается тестом)
    /// либо null: отозванный/истёкший/отсутствующий токен живым не является
    /// (контракт live-only FindLiveByHash, IF-015 — чтение отозванной записи
    /// интерфейсом не выражается). Факт отзыва проверяется исчезновением записи
    /// из живой выборки (с базой Assert.NotNull до отзыва) и поведенчески —
    /// POST /auth/refresh с этим токеном → 401.
    /// </summary>
    public static RefreshToken? RefreshTokenRecord(TestWebAppFactory factory, string tokenValue) =>
        factory.Services.GetRequiredService<ISecurityTokenRepository>()
            .FindLiveByHash(Sha256Hex(tokenValue));

    /// <summary>Снимок KDF-счётчика (тестовый шов IKdfCounter, FR-027/ADR-031).</summary>
    public static IReadOnlyDictionary<string, long> KdfSnapshot(TestWebAppFactory factory) =>
        factory.Services.GetRequiredService<IKdfCounter>().Snapshot();

    /// <summary>Δkdf суммарно по всем меткам между снимками.</summary>
    public static long KdfDelta(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after) =>
        after.Values.Sum() - before.Values.Sum();

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginEndpoint, new { login, password });

    /// <summary>POST /auth/refresh с ЯВНОЙ refresh-cookie (как из браузера).</summary>
    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshTokenValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RefreshEndpoint) { Content = null };
        request.Headers.Add("Cookie", $"{AuthCoreDefaults.RefreshTokenCookieName}={refreshTokenValue}");
        return client.SendAsync(request);
    }

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

    /// <summary>SHA-256 hex (строчные) значения refresh-токена — зеркало TokenService (IF-003).</summary>
    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>
/// FR-016(1): неверный/сверхдлинный/дословный currentPassword — 400 «Неверный
/// текущий пароль» без errors-карты, ДО полевой валидации нового пароля;
/// Δkdf-гейт веток 1 (verify) и 0 (гейт длины &gt;128); хэш не меняется.
/// </summary>
public sealed class MePasswordWrongCurrentTests(MePasswordApiFixture fixture) : IClassFixture<MePasswordApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Put_WrongCurrentWithWeakNew_Returns400WithoutErrors_OneKdf_HashKept()
    {
        // given: пользователь с реальным хэшем; сессия минтована.
        var user = MePasswordEndpointHarness.SeedUser(_factory, "me-pwd-wrong");
        using var client = MePasswordEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // given: база Δkdf ровно вокруг PUT; хранимый хэш зафиксирован.
        var storedBefore = MePasswordEndpointHarness.StoredUser(_factory, user.Id);
        var before = MePasswordEndpointHarness.KdfSnapshot(_factory);

        // when: НЕВЕРНЫЙ currentPassword вместе с НЕВАЛИДНЫМ новым (дискриминирующая
        // комбинация: появление errors.password означало бы обратный порядок).
        using var response = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json(
                """{"currentPassword":"wrong","password":"abc","confirmPassword":"abc"}"""));

        // then: 400 «Неверный текущий пароль» БЕЗ errors-карты.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Неверный текущий пароль", await MePasswordEndpointHarness.MessageAsync(response));
        using var body = await MePasswordEndpointHarness.ReadJsonAsync(response);
        Assert.False(
            body.RootElement.TryGetProperty("errors", out _),
            "Тело 400 неверного currentPassword не должно содержать errors.");

        // then: Δkdf=1 — ровно одна деривация Verify текущего, деривации нового нет.
        Assert.Equal(
            1,
            MePasswordEndpointHarness.KdfDelta(before, MePasswordEndpointHarness.KdfSnapshot(_factory)));

        // then: хранимый passwordHash неизменен.
        var storedAfter = MePasswordEndpointHarness.StoredUser(_factory, user.Id);
        Assert.Equal(storedBefore.PasswordHash, storedAfter.PasswordHash);
    }

    [Fact]
    public async Task Put_OversizedCurrent129_Returns400_ZeroKdf_HashKept()
    {
        // given: пользователь с реальным хэшем; сессия минтована.
        var user = MePasswordEndpointHarness.SeedUser(_factory, "me-pwd-oversized");
        using var client = MePasswordEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        var storedBefore = MePasswordEndpointHarness.StoredUser(_factory, user.Id);
        var before = MePasswordEndpointHarness.KdfSnapshot(_factory);

        // when: currentPassword длиной 129 (граница гейта — 128) с валидной парой нового.
        using var response = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json(
                $$"""{"currentPassword":"{{new string('a', 129)}}","password":"NewPass1!","confirmPassword":"NewPass1!"}"""));

        // then: 400 «Неверный текущий пароль» без errors; Δkdf=0 — БЕЗ выполнения KDF.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Неверный текущий пароль", await MePasswordEndpointHarness.MessageAsync(response));
        using var body = await MePasswordEndpointHarness.ReadJsonAsync(response);
        Assert.False(body.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(
            0,
            MePasswordEndpointHarness.KdfDelta(before, MePasswordEndpointHarness.KdfSnapshot(_factory)));

        var storedAfter = MePasswordEndpointHarness.StoredUser(_factory, user.Id);
        Assert.Equal(storedBefore.PasswordHash, storedAfter.PasswordHash);
    }

    [Fact]
    public async Task Put_CurrentLength128_GoesThroughVerify_Returns400_OneKdf()
    {
        // given: пользователь с реальным хэшем; граница: 128 символов — легальная
        // длина, проверка выполняется (Δkdf=1), в отличие от 129 без KDF.
        var user = MePasswordEndpointHarness.SeedUser(_factory, "me-pwd-len128");
        using var client = MePasswordEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        var before = MePasswordEndpointHarness.KdfSnapshot(_factory);

        // when: НЕВЕРНЫЙ currentPassword длиной ровно 128.
        using var response = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json(
                $$"""{"currentPassword":"{{new string('a', 128)}}","password":"NewPass1!","confirmPassword":"NewPass1!"}"""));

        // then: тот же 400 «Неверный текущий пароль» без errors; Δkdf=1.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Неверный текущий пароль", await MePasswordEndpointHarness.MessageAsync(response));
        using var body = await MePasswordEndpointHarness.ReadJsonAsync(response);
        Assert.False(body.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(
            1,
            MePasswordEndpointHarness.KdfDelta(before, MePasswordEndpointHarness.KdfSnapshot(_factory)));
    }

    [Fact]
    public async Task Put_CurrentWithTrailingSpace_Rejected_ThenVerbatimCurrent_Accepted()
    {
        // given: пользователь с хэшем пароля БЕЗ крайних пробелов.
        var user = MePasswordEndpointHarness.SeedUser(_factory, "me-pwd-verbatim");
        using var client = MePasswordEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when №1: currentPassword с хвостовым пробелом (иная строка — трима нет).
        using var trailing = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json(
                $$"""{"currentPassword":"{{MePasswordEndpointHarness.TestUserPassword}} ","password":"NewPass1!","confirmPassword":"NewPass1!"}"""));

        // then №1: 400 «Неверный текущий пароль»; хэш не изменён.
        Assert.Equal(HttpStatusCode.BadRequest, trailing.StatusCode);
        Assert.Equal("Неверный текущий пароль", await MePasswordEndpointHarness.MessageAsync(trailing));
        var storedAfterFirst = MePasswordEndpointHarness.StoredUser(_factory, user.Id);
        Assert.Equal(user.PasswordHash, storedAfterFirst.PasswordHash);

        // when №2: повторный PUT с ДОСЛОВНЫМ currentPassword.
        using var verbatim = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json(
                $$"""{"currentPassword":"{{MePasswordEndpointHarness.TestUserPassword}}","password":"NewPass1!","confirmPassword":"NewPass1!"}"""));

        // then №2: 204 — дословное сравнение прошло, пароль сменён.
        Assert.Equal(HttpStatusCode.NoContent, verbatim.StatusCode);
        var stored = MePasswordEndpointHarness.StoredUser(_factory, user.Id);
        Assert.NotEqual(user.PasswordHash, stored.PasswordHash);
    }
}

/// <summary>
/// FR-016(3): успешная смена — 204, Δkdf=2 (verify+hash), перезапись хэша;
/// арбитраж ISS-002: refresh-cookie текущего запроса сохраняется, остальные
/// токены отзываются; без refresh-cookie отзываются ВСЕ. Роль не важна
/// (FR-022 role=user).
/// </summary>
public sealed class MePasswordSuccessTests(MePasswordApiFixture fixture) : IClassFixture<MePasswordApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Put_Success_WithRefreshCookie_KeepsCurrentToken_RevokesOtherDevice_AndCountsTwoKdf()
    {
        // given: пользователь; refresh-токены текущей сессии и второго «устройства».
        var user = MePasswordEndpointHarness.SeedUser(_factory, "me-pwd-arb");
        var currentDeviceRefresh = MePasswordEndpointHarness.SeedRefreshToken(_factory, user.Id);
        var otherDeviceRefresh = MePasswordEndpointHarness.SeedRefreshToken(_factory, user.Id);

        // given: оба токена живы до смены (база для последующего Assert.Null —
        // FindLiveByHash live-only: исчезновение записи = отзыв).
        Assert.NotNull(MePasswordEndpointHarness.RefreshTokenRecord(_factory, currentDeviceRefresh));
        Assert.NotNull(MePasswordEndpointHarness.RefreshTokenRecord(_factory, otherDeviceRefresh));

        // given: сессия с access- И refresh-cookie текущего запроса; база Δkdf.
        using var client = MePasswordEndpointHarness.CreateSessionClient(
            _factory, user.Id, UserRoles.Student, refreshTokenValue: currentDeviceRefresh);
        var before = MePasswordEndpointHarness.KdfSnapshot(_factory);

        // when: успешная смена (верный текущий, валидный новый).
        using var response = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json(
                $$"""{"currentPassword":"{{MePasswordEndpointHarness.TestUserPassword}}","password":"NewPass1!","confirmPassword":"NewPass1!"}"""));

        // then: 204; Δkdf=2 (verify текущего + hash нового).
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            2,
            MePasswordEndpointHarness.KdfDelta(before, MePasswordEndpointHarness.KdfSnapshot(_factory)));

        // then: вход по новому паролю — 200, по старому — 401.
        using var newLoginClient = MePasswordEndpointHarness.CreateAnonymousClient(_factory);
        using var newPasswordLogin = await MePasswordEndpointHarness.LoginAsync(
            newLoginClient, "me-pwd-arb", MePasswordEndpointHarness.NewPassword);
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);

        using var oldLoginClient = MePasswordEndpointHarness.CreateAnonymousClient(_factory);
        using var oldPasswordLogin = await MePasswordEndpointHarness.LoginAsync(
            oldLoginClient, "me-pwd-arb", MePasswordEndpointHarness.TestUserPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        // then: refresh токена текущей cookie — 204 (сохранён, ISS-002);
        // refresh второго устройства — 401 (отозван).
        using var currentRefreshClient = MePasswordEndpointHarness.CreateAnonymousClient(_factory);
        using var currentRefresh = await MePasswordEndpointHarness.RefreshAsync(currentRefreshClient, currentDeviceRefresh);
        Assert.Equal(HttpStatusCode.NoContent, currentRefresh.StatusCode);

        using var otherRefreshClient = MePasswordEndpointHarness.CreateAnonymousClient(_factory);
        using var otherRefresh = await MePasswordEndpointHarness.RefreshAsync(otherRefreshClient, otherDeviceRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, otherRefresh.StatusCode);

        // then: в хранилище запись текущего токена жива (арбитраж ISS-002),
        // второй из живой выборки исчез — отозван (CR-001: FindLiveByHash
        // live-only, чтение RevokedAt отозванной записи контрактом недоступно;
        // поведение подтверждено выше — refresh 204/401).
        Assert.NotNull(MePasswordEndpointHarness.RefreshTokenRecord(_factory, currentDeviceRefresh));
        Assert.Null(MePasswordEndpointHarness.RefreshTokenRecord(_factory, otherDeviceRefresh));
    }

    [Fact]
    public async Task Put_Success_WithoutRefreshCookie_RevokesAllRefreshTokens()
    {
        // given: пользователь; в хранилище два его refresh-токена «устройств».
        var user = MePasswordEndpointHarness.SeedUser(_factory, "me-pwd-nocookie");
        var firstDeviceRefresh = MePasswordEndpointHarness.SeedRefreshToken(_factory, user.Id);
        var secondDeviceRefresh = MePasswordEndpointHarness.SeedRefreshToken(_factory, user.Id);

        // given: оба токена живы до смены (база для последующего Assert.Null —
        // FindLiveByHash live-only: исчезновение записи = отзыв).
        Assert.NotNull(MePasswordEndpointHarness.RefreshTokenRecord(_factory, firstDeviceRefresh));
        Assert.NotNull(MePasswordEndpointHarness.RefreshTokenRecord(_factory, secondDeviceRefresh));

        // given: в запросе ТОЛЬКО access-cookie (refresh-cookie нет).
        using var client = MePasswordEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: успешная смена пароля.
        using var response = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json(
                $$"""{"currentPassword":"{{MePasswordEndpointHarness.TestUserPassword}}","password":"NewPass1!","confirmPassword":"NewPass1!"}"""));

        // then: 204; обе записи исчезли из живой выборки — отозваны (CR-001:
        // FindLiveByHash live-only, RevokedAt отозванной записи прочитать
        // нельзя; поведение подтверждено refresh → 401 ниже).
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(MePasswordEndpointHarness.RefreshTokenRecord(_factory, firstDeviceRefresh));
        Assert.Null(MePasswordEndpointHarness.RefreshTokenRecord(_factory, secondDeviceRefresh));

        using var firstRefreshClient = MePasswordEndpointHarness.CreateAnonymousClient(_factory);
        using var firstRefresh = await MePasswordEndpointHarness.RefreshAsync(firstRefreshClient, firstDeviceRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, firstRefresh.StatusCode);

        using var secondRefreshClient = MePasswordEndpointHarness.CreateAnonymousClient(_factory);
        using var secondRefresh = await MePasswordEndpointHarness.RefreshAsync(secondRefreshClient, secondDeviceRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, secondRefresh.StatusCode);
    }

    [Fact]
    public async Task Put_Success_TeacherRole_Allowed_AndHashReplaced()
    {
        // given: авторизованный teacher (FR-022: любая роль); база хэша.
        var user = MePasswordEndpointHarness.SeedUser(
            _factory, "me-pwd-teacher", role: UserRoles.Teacher);
        using var client = MePasswordEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Teacher);

        // when: успешная смена пароля.
        using var response = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json(
                $$"""{"currentPassword":"{{MePasswordEndpointHarness.TestUserPassword}}","password":"NewPass1!","confirmPassword":"NewPass1!"}"""));

        // then: 204; хэш перезаписан и соответствует новому паролю (verify), старый не подходит.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var stored = MePasswordEndpointHarness.StoredUser(_factory, user.Id);
        Assert.NotEqual(user.PasswordHash, stored.PasswordHash);
        Assert.True(hasher.Verify(MePasswordEndpointHarness.NewPassword, stored.PasswordHash, KdfCallers.ChangePassword));
        Assert.False(hasher.Verify(MePasswordEndpointHarness.TestUserPassword, stored.PasswordHash, KdfCallers.ChangePassword));

        // then: прочие поля записи не изменились.
        Assert.Equal(user.Login, stored.Login);
        Assert.Equal(user.Email, stored.Email);
        Assert.Equal(user.FullName, stored.FullName);
        Assert.Equal(UserRoles.Teacher, stored.Role);
        Assert.Equal(user.GroupId, stored.GroupId);
        Assert.Equal(user.CreatedAt, stored.CreatedAt);
    }
}

/// <summary>
/// FR-016(2): валидация password/confirmPassword по FR-006 — 400 «Данные
/// заполнены неверно» + errors {password, confirmPassword}, тексты словаря
/// (включая серверный password.max); битый JSON → 400 без errors, без KDF.
/// </summary>
public sealed class MePasswordValidationTests(MePasswordApiFixture fixture) : IClassFixture<MePasswordApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Put_WeakNewPassword_Returns400_WithMinDigitSpecialErrors()
    {
        // given: пользователь; currentPassword совпадает с текущим.
        var user = MePasswordEndpointHarness.SeedUser(_factory, "me-pwd-weak");
        using var client = MePasswordEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: слабый новый пароль 'abc' (короче 8, без цифры и спецзнака).
        using var response = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json(
                """{"currentPassword":"student123!","password":"abc","confirmPassword":"abc"}"""));

        // then: 400 «Данные заполнены неверно»; errors.password — все три текста.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await MePasswordEndpointHarness.MessageAsync(response));
        var errors = await MePasswordEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            [
                ValidationTexts.PasswordMin,
                ValidationTexts.PasswordDigit,
                ValidationTexts.PasswordSpecial,
            ],
            MePasswordEndpointHarness.ErrorOf(errors, "password"));

        // then: пароль не применён — хранимый хэш прежний.
        var stored = MePasswordEndpointHarness.StoredUser(_factory, user.Id);
        Assert.Equal(user.PasswordHash, stored.PasswordHash);
    }

    [Fact]
    public async Task Put_OversizedNewPassword129_Returns400_WithPasswordMaxOnly()
    {
        // given: пользователь; currentPassword верный.
        var user = MePasswordEndpointHarness.SeedUser(_factory, "me-pwd-newmax");
        using var client = MePasswordEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: новый пароль длиной 129 — серверный текст password.max, состав не проверяется.
        using var response = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json(
                $$"""{"currentPassword":"{{MePasswordEndpointHarness.TestUserPassword}}","password":"{{new string('a', 129)}}","confirmPassword":"{{new string('a', 129)}}"}"""));

        // then: 400; errors.password = ['Пароль — не более 128 символов'].
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await MePasswordEndpointHarness.MessageAsync(response));
        var errors = await MePasswordEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            [ValidationTexts.PasswordMax],
            MePasswordEndpointHarness.ErrorOf(errors, "password"));
    }

    [Fact]
    public async Task Put_ConfirmMismatch_Returns400_MismatchText_HashKept()
    {
        // given: пользователь; currentPassword верный, новый валиден.
        var user = MePasswordEndpointHarness.SeedUser(_factory, "me-pwd-mismatch");
        using var client = MePasswordEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: повтор нового пароля не совпадает.
        using var response = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json(
                $$"""{"currentPassword":"{{MePasswordEndpointHarness.TestUserPassword}}","password":"NewPass1!","confirmPassword":"Other1!x"}"""));

        // then: 400; errors.confirmPassword дословно password.mismatch.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await MePasswordEndpointHarness.MessageAsync(response));
        var errors = await MePasswordEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            [ValidationTexts.PasswordMismatch],
            MePasswordEndpointHarness.ErrorOf(errors, "confirmPassword"));

        // then: пароль не изменён.
        var stored = MePasswordEndpointHarness.StoredUser(_factory, user.Id);
        Assert.Equal(user.PasswordHash, stored.PasswordHash);
    }

    [Fact]
    public async Task Put_MissingNewPasswordFields_Returns400_WithBothFieldErrors()
    {
        // given: пользователь; currentPassword верный; валидный JSON без password/confirmPassword.
        var user = MePasswordEndpointHarness.SeedUser(_factory, "me-pwd-missing");
        using var client = MePasswordEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT {"currentPassword":"student123!"}.
        using var response = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json("""{"currentPassword":"student123!"}"""));

        // then: 400; ошибки ОБЕИХ полей сразу: password (min/digit/letter/special
        // пустой строки) и confirmPassword (required).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await MePasswordEndpointHarness.MessageAsync(response));
        var errors = await MePasswordEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            [
                ValidationTexts.PasswordMin,
                ValidationTexts.PasswordDigit,
                ValidationTexts.PasswordLetter,
                ValidationTexts.PasswordSpecial,
            ],
            MePasswordEndpointHarness.ErrorOf(errors, "password"));
        Assert.Equal(
            [ValidationTexts.Required],
            MePasswordEndpointHarness.ErrorOf(errors, "confirmPassword"));
    }

    [Fact]
    public async Task Put_MalformedJson_Returns400WithoutErrors_ZeroKdf_HashKept()
    {
        // given: пользователь с реальным хэшем; сессия минтована.
        var user = MePasswordEndpointHarness.SeedUser(_factory, "me-pwd-broken");
        using var client = MePasswordEndpointHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        var before = MePasswordEndpointHarness.KdfSnapshot(_factory);

        // when: синтаксически некорректный JSON тела.
        using var response = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json("""{"currentPassword": """));

        // then: 400 «Данные заполнены неверно» БЕЗ errors; Δkdf=0; хэш прежний.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await MePasswordEndpointHarness.MessageAsync(response));
        using var body = await MePasswordEndpointHarness.ReadJsonAsync(response);
        Assert.False(
            body.RootElement.TryGetProperty("errors", out _),
            "Тело 400 битого JSON не должно содержать errors.");
        Assert.Equal(
            0,
            MePasswordEndpointHarness.KdfDelta(before, MePasswordEndpointHarness.KdfSnapshot(_factory)));
        var stored = MePasswordEndpointHarness.StoredUser(_factory, user.Id);
        Assert.Equal(user.PasswordHash, stored.PasswordHash);
    }
}

/// <summary>FR-022: эндпойнт только для авторизованных — анонимно 401 «Не авторизован».</summary>
public sealed class MePasswordAccessTests(MePasswordApiFixture fixture) : IClassFixture<MePasswordApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Put_Anonymous_ReturnsDeterministicUnauthorizedEnvelope()
    {
        // when: PUT /me/password без access-cookie.
        using var client = MePasswordEndpointHarness.CreateAnonymousClient(_factory);
        using var response = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json("""{"currentPassword":"x","password":"NewPass1!","confirmPassword":"NewPass1!"}"""));

        // then: 401 {'message':'Не авторизован'} (FR-022, IF-001).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await MePasswordEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Put_DeletedUserWithLiveAccess_Returns401Envelope()
    {
        // given: access-JWT минтован для пользователя, которого нет в хранилище
        // (удалён при живом access-токене — зеркало мока requireSessionUser).
        using var client = MePasswordEndpointHarness.CreateSessionClient(
            _factory, Guid.NewGuid(), UserRoles.Student);

        // when: PUT /me/password с валидным по форме телом.
        using var response = await client.PutAsync(
            MePasswordEndpointHarness.PasswordEndpoint,
            MePasswordEndpointHarness.Json("""{"currentPassword":"x","password":"NewPass1!","confirmPassword":"NewPass1!"}"""));

        // then: 401 {'message':'Не авторизован'} от действия (FR-022: 401 раньше 400).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await MePasswordEndpointHarness.MessageAsync(response));
    }
}
