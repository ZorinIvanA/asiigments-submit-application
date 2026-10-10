using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.Hosting.Configuration;
using LabsApp.Observability;
using LabsApp.Storage;
using LabsApp.Tests.Hosting;
using LabsApp.Tests.Observability;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ValidationTexts = LabsApp.Domain.Validation.ErrorTexts;

namespace LabsApp.Tests.Recovery;

// ============================================================================
// Эндпойнт-тесты RecoveryController (C-006, IF-008; FR-012/FR-013/FR-014 +
// лимит recovery_request FR-004 + Δkdf-гейт FR-027). Новая зона Recovery* —
// единственный писатель файлов. Сессии не нужны: все три эндпойнта анонимны
// (FR-022). Пользователи сидуются DI с реальным PBKDF2-хэшем (ADR-015);
// recovery-коды, reset- и refresh-токены — через ITokenService +
// ISecurityTokenRepository хоста (ADR-014/IF-003/IF-015). Значение кода
// восстановления известно тесту ТОЛЬКО из log-sink (категория «EmailDev»,
// маркер [DEV-EMAIL] — IF-005): извлечение и проверка NFR-006 в тех же тестах.
// Гейты Δkdf — снимки IKdfCounter.Snapshot() до/после измеряемого запроса.
// ============================================================================

/// <summary>Фикстура хоста: без демо-набора, тестовые итерации KDF (1000).</summary>
public sealed class RecoveryApiFixture : IDisposable
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
/// Харнес recovery-эндпойнтов: DI-сид пользователей/токенов/кодов, чтение
// log-sink, снимки KDF-счётчика, чтение JSON-тел и конверта ошибок IF-001.
/// </summary>
internal static class RecoveryEndpointHarness
{
    public const string RequestEndpoint = "/api/v1/auth/recovery/request";
    public const string ConfirmEndpoint = "/api/v1/auth/recovery/confirm";
    public const string ResetEndpoint = "/api/v1/auth/reset-password";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RefreshEndpoint = "/api/v1/auth/refresh";

    /// <summary>Пароль DI-сид-пользователей до сброса (правила FR-006 соблюдены).</summary>
    public const string TestUserPassword = "student123!";

    /// <summary>Валидный новый пароль сброса (буквально из when кейсов FR-014).</summary>
    public const string NewPassword = "NewPass1!";

    public static HttpClient CreateAnonymousClient(TestWebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>DI-сид пользователя с РЕАЛЬНЫМ PBKDF2-хэшем пароля (метка seed).</summary>
    public static User SeedUser(
        TestWebAppFactory factory,
        string login,
        string? email = null,
        string role = UserRoles.Student)
    {
        var passwordHash = factory.Services
            .GetRequiredService<IPasswordHasher>()
            .Hash(TestUserPassword, KdfCallers.Seed);
        var repository = factory.Services.GetRequiredService<IUserRepository>();
        repository.Add(new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email ?? $"{login}@example.com",
            PasswordHash = passwordHash,
            FullName = "Тест Тестович Тестов",
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

    /// <summary>
    /// Ранее выданный reset-токен (DI-сид): значение возвращается открытым,
    /// в хранилище — только SHA-256-дайджест. <paramref name="timeToLive"/>
    /// переопределяет TTL сид-записи (отрицательный — уже просроченный).
    /// </summary>
    public static string SeedResetToken(TestWebAppFactory factory, Guid userId, TimeSpan? timeToLive = null)
    {
        var grant = factory.Services
            .GetRequiredService<ITokenService>()
            .CreatePasswordResetToken(userId);

        factory.Services.GetRequiredService<ISecurityTokenRepository>().Add(new PasswordResetToken
        {
            TokenHash = grant.TokenHash,
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.Add(timeToLive ?? TimeSpan.FromMinutes(15)),
            UsedAt = null,
        });

        return grant.Value;
    }

    /// <summary>
    /// Ранее выданный код восстановления с известным значением (DI-сид):
    /// attempts и TTL переопределяемы (отрицательный TTL — уже просроченный).
    /// </summary>
    public static void SeedRecoveryCode(
        TestWebAppFactory factory,
        Guid userId,
        string code,
        int attempts = 0,
        TimeSpan? timeToLive = null)
    {
        var now = DateTime.UtcNow;
        factory.Services.GetRequiredService<ISecurityTokenRepository>().AddLive(new RecoveryCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = factory.Services.GetRequiredService<ITokenService>().HashRecoveryCode(code),
            ExpiresAt = now.Add(timeToLive ?? TimeSpan.FromMinutes(10)),
            UsedAt = null,
            Attempts = attempts,
            CreatedAt = now,
        });
    }

    /// <summary>Гашение сид-токена без HTTP (ветка «использован», FR-014).</summary>
    public static void MarkResetTokenUsed(TestWebAppFactory factory, string tokenValue) =>
        factory.Services.GetRequiredService<ISecurityTokenRepository>()
            .MarkUsed(Sha256Hex(tokenValue));

    /// <summary>Живой код пользователя либо null (коды с attempts и TTL — копия-снимок).</summary>
    public static RecoveryCode? LiveCode(TestWebAppFactory factory, Guid userId) =>
        factory.Services.GetRequiredService<ISecurityTokenRepository>().FindLiveForUser(userId);

    /// <summary>
    /// ЖИВАЯ запись reset-токена по ОТКРЫТОМУ значению либо null: применённый или
    /// истёкший токен живым не является (live-only FindLiveResetByHash, IF-015) —
    /// факт гашения проверяется исчезновением из живой выборки (с базой NotNull).
    /// </summary>
    public static PasswordResetToken? ResetTokenRecord(TestWebAppFactory factory, string tokenValue) =>
        factory.Services.GetRequiredService<ISecurityTokenRepository>()
            .FindLiveResetByHash(Sha256Hex(tokenValue));

    /// <summary>Живая запись refresh-токена по значению либо null (live-only, IF-015).</summary>
    public static RefreshToken? RefreshTokenRecord(TestWebAppFactory factory, string tokenValue) =>
        factory.Services.GetRequiredService<ISecurityTokenRepository>()
            .FindLiveByHash(Sha256Hex(tokenValue));

    /// <summary>Записи категории «EmailDev» log-sink тестового хоста (IF-005).</summary>
    public static IReadOnlyList<TestLogRecord> EmailDevRecords(TestWebAppFactory factory) =>
        [.. factory.LogSink.Snapshot().Where(record => record.Category == DevEmailSender.LogCategory)];

    /// <summary>
    /// Код восстановления из ПОСЛЕДНЕЙ записи «EmailDev»: в записи — ровно один
    /// 6-значный код (маркер [DEV-EMAIL] проверяется тестами NFR-006).
    /// </summary>
    public static string LastEmailedCode(TestWebAppFactory factory)
    {
        var records = EmailDevRecords(factory);
        Assert.True(records.Count > 0, "Не найдено записей категории «EmailDev».");
        Assert.Contains(DevEmailSender.DevEmailMarker, records[^1].Message, StringComparison.Ordinal);

        var codes = Regex.Matches(records[^1].Message, "[0-9]{6}")
            .Select(match => match.Value)
            .Distinct()
            .ToList();
        Assert.True(
            codes.Count == 1,
            $"В записи «EmailDev» ожидался ровно один 6-значный код: {records[^1].Message}");
        return codes[0];
    }

    /// <summary>Снимок KDF-счётчика (тестовый шов IKdfCounter, FR-027/ADR-031).</summary>
    public static IReadOnlyDictionary<string, long> KdfSnapshot(TestWebAppFactory factory) =>
        factory.Services.GetRequiredService<IKdfCounter>().Snapshot();

    /// <summary>Δkdf суммарно по всем меткам между снимками.</summary>
    public static long KdfDelta(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after) =>
        after.Values.Sum() - before.Values.Sum();

    /// <summary>Δkdf по ОДНОЙ метке вызывателя (гейт Δkdf(reset_password), FR-027).</summary>
    public static long KdfDeltaOfCaller(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after,
        string caller) =>
        after.GetValueOrDefault(caller) - before.GetValueOrDefault(caller);

    public static Task<HttpResponseMessage> RequestCodeAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync(RequestEndpoint, new { email });

    public static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string email, string code) =>
        client.PostAsJsonAsync(ConfirmEndpoint, new { email, code });

    public static Task<HttpResponseMessage> ResetAsync(
        HttpClient client,
        string resetToken,
        string password,
        string confirmPassword) =>
        client.PostAsJsonAsync(ResetEndpoint, new { resetToken, password, confirmPassword });

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginEndpoint, new { login, password });

    /// <summary>POST /auth/refresh с ЯВНОЙ refresh-cookie (как из браузера).</summary>
    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshTokenValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RefreshEndpoint) { Content = null };
        request.Headers.Add("Cookie", $"{AuthCoreDefaults.RefreshTokenCookieName}={refreshTokenValue}");
        return client.SendAsync(request);
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

    /// <summary>Тело ответа пустое (0 байт): ветки 200/204 без контента (ISS-014).</summary>
    public static async Task AssertEmptyBodyAsync(HttpResponseMessage response)
    {
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.True(
            response.Content.Headers.ContentLength is 0 or null,
            $"Ожидался пустой Content-Length, фактически {response.Content.Headers.ContentLength}.");
    }

    /// <summary>SHA-256 hex (строчные) значения токена — зеркало TokenService (IF-003).</summary>
    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>
/// FR-012: request — всегда 200 с ПУСТЫМ телом (0 байт, НЕ '{}'), ровно один
/// живой код TTL 10 минут, письмо в «EmailDev» для обеих веток, Δkdf=0;
/// лимит 3/час одинаков для существующего и несуществующего email (FR-004);
/// переотправка гасит прежний код; код появляется только в категории «EmailDev».
/// </summary>
public sealed class RecoveryRequestTests(RecoveryApiFixture fixture) : IClassFixture<RecoveryApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Request_ExistingEmail_200EmptyBody_SingleLiveCode_TtlTenMinutes_EmailOnce_ZeroKdf()
    {
        // given: зарегистрированный пользователь; чистый log-sink; база Δkdf.
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-req-exist");
        _factory.LogSink.Clear();
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        var before = RecoveryEndpointHarness.KdfSnapshot(_factory);

        // when: запрос кода.
        using var response = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);

        // then: 200 с ПУСТЫМ телом — 0 байт, НЕ '{}' (ISS-014/ADR-012).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await RecoveryEndpointHarness.AssertEmptyBodyAsync(response);

        // then: ровно один живой код, attempts=0, TTL 10 минут.
        var live = RecoveryEndpointHarness.LiveCode(_factory, user.Id);
        Assert.NotNull(live);
        Assert.Null(live!.UsedAt);
        Assert.Equal(0, live.Attempts);
        Assert.Equal(TimeSpan.FromMinutes(10), live.ExpiresAt - live.CreatedAt);

        // then: IEmailSender вызван один раз (одна запись «EmailDev»); Δkdf=0.
        Assert.Single(RecoveryEndpointHarness.EmailDevRecords(_factory));
        Assert.Equal(
            0,
            RecoveryEndpointHarness.KdfDelta(before, RecoveryEndpointHarness.KdfSnapshot(_factory)));
    }

    [Fact]
    public async Task Request_NonexistentEmail_200EmptyBody_EmailSentSameWay()
    {
        // given: адреса нет в хранилище; лимит не исчерпан; чистый log-sink.
        _factory.LogSink.Clear();
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);

        // when: запрос кода для несуществующего email.
        using var response = await RecoveryEndpointHarness.RequestCodeAsync(client, "nobody@example.com");

        // then: 200 пустое тело; отправка («EmailDev») выполнена так же, как для
        // существующего — ровно одна запись с маркером [DEV-EMAIL] (IF-005).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await RecoveryEndpointHarness.AssertEmptyBodyAsync(response);
        var devRecords = RecoveryEndpointHarness.EmailDevRecords(_factory);
        Assert.Single(devRecords);
        Assert.Contains(DevEmailSender.DevEmailMarker, devRecords[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_Resend_ExtinguishesPreviousCode_OldRejected_NewConfirmed()
    {
        // given: пользователь; первый код C1 получен и вычитан из «EmailDev».
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-req-resend");
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        _factory.LogSink.Clear();

        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);
        var firstCode = RecoveryEndpointHarness.LastEmailedCode(_factory);

        // when: повторный request того же email.
        using var second = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);
        var secondCode = RecoveryEndpointHarness.LastEmailedCode(_factory);

        // then: 200; новый код отличен от прежнего и жив; прежний погашен.
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotEqual(firstCode, secondCode);
        var live = RecoveryEndpointHarness.LiveCode(_factory, user.Id);
        Assert.NotNull(live);

        // then: подтверждение прежнего кода C1 → 400 «Код восстановления не подходит»;
        // новый C2 подтверждается → 200 {resetToken}.
        using var oldConfirm = await RecoveryEndpointHarness.ConfirmAsync(client, user.Email, firstCode);
        Assert.Equal(HttpStatusCode.BadRequest, oldConfirm.StatusCode);
        Assert.Equal(ValidationTexts.RecoveryCodeRejected, await RecoveryEndpointHarness.MessageAsync(oldConfirm));

        using var newConfirm = await RecoveryEndpointHarness.ConfirmAsync(client, user.Email, secondCode);
        Assert.Equal(HttpStatusCode.OK, newConfirm.StatusCode);
    }

    [Fact]
    public async Task Request_FourthAttemptNonexistentEmail_429_UnifiedMessage()
    {
        // given: 3 request на несуществующий email X за час.
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        const string email = "rec-429-unknown@example.com";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var acquired = await RecoveryEndpointHarness.RequestCodeAsync(client, email);
            Assert.Equal(HttpStatusCode.OK, acquired.StatusCode);
        }

        // when: 4-й request на тот же несуществующий email.
        using var response = await RecoveryEndpointHarness.RequestCodeAsync(client, email);

        // then: 429 «Слишком много попыток. Повторите позже».
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(ValidationTexts.RateLimited, await RecoveryEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Request_FourthAttemptExistingEmail_429_UnifiedMessage()
    {
        // given: существующий email; 3 request с ДРУГИМ регистром (тот же
        // ci-ключ lower(trim(email))) — лимитер не различает ветки.
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-429-exist");
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var acquired = await RecoveryEndpointHarness.RequestCodeAsync(
                client, $"  REC-429-EXIST@Example.Com ");
            Assert.Equal(HttpStatusCode.OK, acquired.StatusCode);
        }

        // when: 4-й request на существующий email.
        using var response = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);

        // then: тот же 429 с тем же сообщением, что и для несуществующего (оракула нет).
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(ValidationTexts.RateLimited, await RecoveryEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Request_EmptyAndMalformedBody_Always200EmptyBody()
    {
        // given: чистый log-sink (письмо уходит и для пустого/мусорного email).
        _factory.LogSink.Clear();
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);

        // when №1: запрос с ПУСТЫМ телом.
        using var emptyRequest = new HttpRequestMessage(HttpMethod.Post, RecoveryEndpointHarness.RequestEndpoint);
        using var emptyResponse = await client.SendAsync(emptyRequest);

        // then №1: 200 пустое тело (ответ ВСЕГДА одинаков, FR-012).
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        await RecoveryEndpointHarness.AssertEmptyBodyAsync(emptyResponse);

        // when №2: запрос с синтаксически некорректным JSON.
        using var malformedResponse = await client.PostAsync(
            RecoveryEndpointHarness.RequestEndpoint,
            RecoveryEndpointHarness.Json("""{"email": """));

        // then №2: снова 200 пустое тело.
        Assert.Equal(HttpStatusCode.OK, malformedResponse.StatusCode);
        await RecoveryEndpointHarness.AssertEmptyBodyAsync(malformedResponse);

        // then: письмо «отправлено» для обеих попыток (формат записи одинаков).
        Assert.Equal(2, RecoveryEndpointHarness.EmailDevRecords(_factory).Count);
    }

    [Fact]
    public async Task Request_CodeAppearsOnlyInEmailDevCategory()
    {
        // given: пользователь; чистый log-sink; Development-хост (фикстура).
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-req-nfr006");
        _factory.LogSink.Clear();
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);

        // when: запрос кода.
        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);

        // then: код из записи «EmailDev» не встречается НИ В ОДНОЙ записи других
        // категорий (NFR-006/ISS-003); маркер [DEV-EMAIL] на месте.
        var code = RecoveryEndpointHarness.LastEmailedCode(_factory);
        Assert.Matches("^[0-9]{6}$", code);
        var otherRecords = _factory.LogSink.Snapshot()
            .Where(record => record.Category != DevEmailSender.LogCategory)
            .ToList();
        Assert.All(
            otherRecords,
            record => Assert.DoesNotContain(code, record.Message, StringComparison.Ordinal));
    }
}

/// <summary>
/// FR-013: confirm — верный/неверный (attempts инкремент)/5-я неверная гасит
/// код/просроченный/неизвестный email — единый текст «Код восстановления не
/// подходит»; успех: 200 {resetToken} ≥256 бит, TTL 15 минут, код погашен.
/// </summary>
public sealed class RecoveryConfirmTests(RecoveryApiFixture fixture) : IClassFixture<RecoveryApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Confirm_CorrectCode_Trimmed_ReturnsResetToken_LiveFifteenMinutes_CodeConsumed()
    {
        // given: живой код известен из «EmailDev».
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-conf-ok");
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);
        var code = RecoveryEndpointHarness.LastEmailedCode(_factory);

        // when: confirm с пробелами вокруг email и code (триммятся, IF-008).
        using var response = await RecoveryEndpointHarness.ConfirmAsync(
            client, $"  {user.Email} ", $"  {code} ");

        // then: 200 {resetToken: непустая строка ≥43 симв. (base64url 32 байт — ≥256 бит)};
        // в хранилище живая запись с TTL 15 минут (ASM-002/IF-003).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await RecoveryEndpointHarness.ReadJsonAsync(response);
        var resetToken = body.RootElement.GetProperty("resetToken").GetString();
        Assert.False(string.IsNullOrEmpty(resetToken));
        Assert.True(resetToken!.Length >= 43, $"Ожидался токен ≥256 бит, длина {resetToken.Length}.");

        var record = RecoveryEndpointHarness.ResetTokenRecord(_factory, resetToken!);
        Assert.NotNull(record);
        Assert.Equal(user.Id, record!.UserId);
        var remaining = record.ExpiresAt - DateTime.UtcNow;
        Assert.True(remaining > TimeSpan.FromMinutes(14), $"TTL сбился: {remaining}.");
        Assert.True(remaining <= TimeSpan.FromMinutes(15).Add(TimeSpan.FromSeconds(5)), $"TTL сбился: {remaining}.");

        // then: код погашен — повторный confirm с верным кодом → 400.
        using var repeat = await RecoveryEndpointHarness.ConfirmAsync(client, user.Email, code);
        Assert.Equal(HttpStatusCode.BadRequest, repeat.StatusCode);
        Assert.Equal(ValidationTexts.RecoveryCodeRejected, await RecoveryEndpointHarness.MessageAsync(repeat));
    }

    [Fact]
    public async Task Confirm_WrongCode_IncrementsAttempts_UnifiedText()
    {
        // given: живой код; заведомо неверное значение (7 символов ≠ коду).
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-conf-wrong");
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);
        var code = RecoveryEndpointHarness.LastEmailedCode(_factory);

        // when: неверный код.
        using var response = await RecoveryEndpointHarness.ConfirmAsync(client, user.Email, $"9{code}");

        // then: 400 единый текст; attempts живого кода = 1 (попытки считаются).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.RecoveryCodeRejected, await RecoveryEndpointHarness.MessageAsync(response));
        var live = RecoveryEndpointHarness.LiveCode(_factory, user.Id);
        Assert.NotNull(live);
        Assert.Equal(1, live!.Attempts);
    }

    [Fact]
    public async Task Confirm_FifthWrongAttempt_AnnullsCode_CorrectCodeThenRejected()
    {
        // given: живой код с attempts=4 (DI-сид, значение известно).
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-conf-fifth");
        RecoveryEndpointHarness.SeedRecoveryCode(_factory, user.Id, "123456", attempts: 4);
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);

        // when: пятая неверная попытка.
        using var wrong = await RecoveryEndpointHarness.ConfirmAsync(client, user.Email, "654321");

        // then: 400 «Код восстановления не подходит»; код погашен (живых нет);
        // последующий ВЕРНЫЙ код → тот же 400 (аннулированный не «оживает»).
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(ValidationTexts.RecoveryCodeRejected, await RecoveryEndpointHarness.MessageAsync(wrong));
        Assert.Null(RecoveryEndpointHarness.LiveCode(_factory, user.Id));

        using var correct = await RecoveryEndpointHarness.ConfirmAsync(client, user.Email, "123456");
        Assert.Equal(HttpStatusCode.BadRequest, correct.StatusCode);
        Assert.Equal(ValidationTexts.RecoveryCodeRejected, await RecoveryEndpointHarness.MessageAsync(correct));
    }

    [Fact]
    public async Task Confirm_UnknownEmail_UnifiedText()
    {
        // when: confirm для незарегистрированного email.
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        using var response = await RecoveryEndpointHarness.ConfirmAsync(client, "no@no.no", "123456");

        // then: 400 с ТЕМ ЖЕ текстом, что и при неверном коде (без раскрытия существования).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.RecoveryCodeRejected, await RecoveryEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Confirm_ExpiredCode_UnifiedText()
    {
        // given: просроченный код (TTL −1 минута, DI-сид).
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-conf-expired");
        RecoveryEndpointHarness.SeedRecoveryCode(
            _factory, user.Id, "123456", timeToLive: TimeSpan.FromMinutes(-1));
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);

        // when: confirm верным значением просроченного кода.
        using var response = await RecoveryEndpointHarness.ConfirmAsync(client, user.Email, "123456");

        // then: 400 единый текст (просроченный = не живой, FR-013).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.RecoveryCodeRejected, await RecoveryEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Confirm_MalformedJson_400WithoutErrors()
    {
        // when: синтаксически некорректный JSON тела.
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        using var response = await client.PostAsync(
            RecoveryEndpointHarness.ConfirmEndpoint,
            RecoveryEndpointHarness.Json("""{"email": """));

        // then: 400 «Данные заполнены неверно» БЕЗ errors-карты (IF-001).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.InvalidData, await RecoveryEndpointHarness.MessageAsync(response));
        using var body = await RecoveryEndpointHarness.ReadJsonAsync(response);
        Assert.False(body.RootElement.TryGetProperty("errors", out _));
    }
}

/// <summary>
/// FR-014: reset-password — успех (204, Δkdf(reset_password)=1, пароль сменён,
/// ВСЕ reset-токены погашены, ВСЕ refresh-токены отозваны); несуществующий/
/// просроченный (гасится)/использованный токен → 400 «Ссылка восстановления
/// недействительна или истекла»; полевая ошибка не гасит токен (Δkdf=0),
/// 129 символов → errors.password=['Пароль — не более 128 символов'].
/// </summary>
public sealed class RecoveryResetTests(RecoveryApiFixture fixture) : IClassFixture<RecoveryApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Reset_FullFlow_204_OldPasswordFails_NewWorks_TokensConsumed_RefreshRevoked_DeltaKdfOne()
    {
        // given: пользователь; refresh-токены двух «устройств»; ранее выданный
        // второй reset-токен (погашается успехом — ConsumeAllForUser); чужой
        // пользователь с живым refresh-токеном (сессии других не затрагиваются).
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-reset-flow");
        var firstDevice = RecoveryEndpointHarness.SeedRefreshToken(_factory, user.Id);
        var secondDevice = RecoveryEndpointHarness.SeedRefreshToken(_factory, user.Id);
        var earlierResetToken = RecoveryEndpointHarness.SeedResetToken(_factory, user.Id);
        var otherUser = RecoveryEndpointHarness.SeedUser(_factory, "rec-reset-other");
        var otherDevice = RecoveryEndpointHarness.SeedRefreshToken(_factory, otherUser.Id);

        Assert.NotNull(RecoveryEndpointHarness.RefreshTokenRecord(_factory, firstDevice));
        Assert.NotNull(RecoveryEndpointHarness.ResetTokenRecord(_factory, earlierResetToken));

        // given: живой код → confirm → resetToken; база Δkdf вокруг reset.
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);
        var code = RecoveryEndpointHarness.LastEmailedCode(_factory);
        using var confirm = await RecoveryEndpointHarness.ConfirmAsync(client, user.Email, code);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        using var confirmBody = await RecoveryEndpointHarness.ReadJsonAsync(confirm);
        var resetToken = confirmBody.RootElement.GetProperty("resetToken").GetString();
        Assert.False(string.IsNullOrEmpty(resetToken));

        var before = RecoveryEndpointHarness.KdfSnapshot(_factory);

        // when: сброс пароля; resetToken с пробелами (триммится, IF-008).
        using var response = await RecoveryEndpointHarness.ResetAsync(
            client, $" {resetToken} ", RecoveryEndpointHarness.NewPassword, RecoveryEndpointHarness.NewPassword);

        // then: 204 с пустым телом; Δkdf=1, ровно с меткой reset_password.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await RecoveryEndpointHarness.AssertEmptyBodyAsync(response);
        var after = RecoveryEndpointHarness.KdfSnapshot(_factory);
        Assert.Equal(1, RecoveryEndpointHarness.KdfDelta(before, after));
        Assert.Equal(
            1,
            RecoveryEndpointHarness.KdfDeltaOfCaller(before, after, KdfCallers.ResetPassword));

        // then: вход со старым паролем — 401, с новым — 200.
        using var oldLoginClient = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        using var oldLogin = await RecoveryEndpointHarness.LoginAsync(
            oldLoginClient, user.Login, RecoveryEndpointHarness.TestUserPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        using var newLoginClient = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        using var newLogin = await RecoveryEndpointHarness.LoginAsync(
            newLoginClient, user.Login, RecoveryEndpointHarness.NewPassword);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        // then: ВСЕ reset-токены пользователя погашены — и предъявленный, и ранее
        // выданный (live-only выборка: исчезновение записи = погашение).
        Assert.Null(RecoveryEndpointHarness.ResetTokenRecord(_factory, resetToken!));
        Assert.Null(RecoveryEndpointHarness.ResetTokenRecord(_factory, earlierResetToken));

        // then: ВСЕ его refresh-токены отозваны (живая выборка + поведение refresh 401).
        Assert.Null(RecoveryEndpointHarness.RefreshTokenRecord(_factory, firstDevice));
        Assert.Null(RecoveryEndpointHarness.RefreshTokenRecord(_factory, secondDevice));
        using var refreshClient = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        using var refresh = await RecoveryEndpointHarness.RefreshAsync(refreshClient, firstDevice);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // then: чужая сессия не затронута.
        Assert.NotNull(RecoveryEndpointHarness.RefreshTokenRecord(_factory, otherDevice));
    }

    [Fact]
    public async Task Reset_UnknownToken_400ResetLinkInvalid_ZeroKdf_PasswordKept()
    {
        // given: пользователь с реальным хэшем; в хранилище нет такого дайджеста.
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-reset-unknown");
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        var before = RecoveryEndpointHarness.KdfSnapshot(_factory);

        // when: reset с несуществующим токеном и валидной парой паролей.
        using var response = await RecoveryEndpointHarness.ResetAsync(
            client, "garbage", RecoveryEndpointHarness.NewPassword, RecoveryEndpointHarness.NewPassword);

        // then: 400 «Ссылка восстановления недействительна или истекла»; Δkdf=0;
        // пароль не изменён.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.ResetTokenInvalid, await RecoveryEndpointHarness.MessageAsync(response));
        Assert.Equal(
            0,
            RecoveryEndpointHarness.KdfDelta(before, RecoveryEndpointHarness.KdfSnapshot(_factory)));
        var stored = _factory.Services.GetRequiredService<IUserRepository>().GetById(user.Id);
        Assert.NotNull(stored);
        Assert.True(_factory.Services.GetRequiredService<IPasswordHasher>()
            .Verify(RecoveryEndpointHarness.TestUserPassword, stored!.PasswordHash, KdfCallers.ResetPassword));
    }

    [Fact]
    public async Task Reset_ExpiredToken_400ResetLinkInvalid_PasswordKept()
    {
        // given: просроченный reset-токен (TTL −1 минута, DI-сид).
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-reset-expired");
        var expiredToken = RecoveryEndpointHarness.SeedResetToken(
            _factory, user.Id, TimeSpan.FromMinutes(-1));
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);

        // when: reset просроченным токеном.
        using var response = await RecoveryEndpointHarness.ResetAsync(
            client, expiredToken, RecoveryEndpointHarness.NewPassword, RecoveryEndpointHarness.NewPassword);

        // then: 400 RESET_LINK_INVALID; токен неживой (гашение найденного
        // неживого через интерфейс наблюдается как отсутствие в живой выборке);
        // пароль не изменён.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.ResetTokenInvalid, await RecoveryEndpointHarness.MessageAsync(response));
        Assert.Null(RecoveryEndpointHarness.ResetTokenRecord(_factory, expiredToken));
        var stored = _factory.Services.GetRequiredService<IUserRepository>().GetById(user.Id);
        Assert.NotNull(stored);
        Assert.True(_factory.Services.GetRequiredService<IPasswordHasher>()
            .Verify(RecoveryEndpointHarness.TestUserPassword, stored!.PasswordHash, KdfCallers.ResetPassword));
    }

    [Fact]
    public async Task Reset_UsedToken_400ResetLinkInvalid_PasswordKept()
    {
        // given: применённый reset-токен (DI-сид + гашение хранилищем).
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-reset-used");
        var usedToken = RecoveryEndpointHarness.SeedResetToken(_factory, user.Id);
        RecoveryEndpointHarness.MarkResetTokenUsed(_factory, usedToken);
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);

        // when: reset использованным токеном.
        using var response = await RecoveryEndpointHarness.ResetAsync(
            client, usedToken, RecoveryEndpointHarness.NewPassword, RecoveryEndpointHarness.NewPassword);

        // then: 400 RESET_LINK_INVALID; пароль не изменён.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.ResetTokenInvalid, await RecoveryEndpointHarness.MessageAsync(response));
        var stored = _factory.Services.GetRequiredService<IUserRepository>().GetById(user.Id);
        Assert.NotNull(stored);
        Assert.True(_factory.Services.GetRequiredService<IPasswordHasher>()
            .Verify(RecoveryEndpointHarness.TestUserPassword, stored!.PasswordHash, KdfCallers.ResetPassword));
    }

    [Fact]
    public async Task Reset_FieldError_KeepsTokenLive_ZeroKdf_ThenSucceeds()
    {
        // given: живой reset-токен; база Δkdf.
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-reset-weak");
        var resetToken = RecoveryEndpointHarness.SeedResetToken(_factory, user.Id);
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        var before = RecoveryEndpointHarness.KdfSnapshot(_factory);

        // when: слабый пароль 'abc' (короче 8, без цифры и спецзнака).
        using var weak = await RecoveryEndpointHarness.ResetAsync(client, resetToken, "abc", "abc");

        // then: 400 «Данные заполнены неверно» + errors.password (min/digit/special);
        // Δkdf=0; токен ОСТАЛСЯ живым (полевая ошибка не гасит, FR-014).
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(ValidationTexts.InvalidData, await RecoveryEndpointHarness.MessageAsync(weak));
        var errors = await RecoveryEndpointHarness.ErrorsAsync(weak);
        Assert.Equal(
            [
                ValidationTexts.PasswordMin,
                ValidationTexts.PasswordDigit,
                ValidationTexts.PasswordSpecial,
            ],
            RecoveryEndpointHarness.ErrorOf(errors, "password"));
        Assert.Equal(
            0,
            RecoveryEndpointHarness.KdfDelta(before, RecoveryEndpointHarness.KdfSnapshot(_factory)));
        Assert.NotNull(RecoveryEndpointHarness.ResetTokenRecord(_factory, resetToken));

        // then: повтор с валидной парой — 204 (токен применим).
        using var ok = await RecoveryEndpointHarness.ResetAsync(
            client, resetToken, RecoveryEndpointHarness.NewPassword, RecoveryEndpointHarness.NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Reset_OversizedPassword129_400PasswordMaxOnly_TokenLive_ZeroKdf()
    {
        // given: живой reset-токен; база Δkdf.
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-reset-max");
        var resetToken = RecoveryEndpointHarness.SeedResetToken(_factory, user.Id);
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        var before = RecoveryEndpointHarness.KdfSnapshot(_factory);

        // when: пароль длиной 129 (confirmPassword совпадает).
        var oversized = new string('a', 129);
        using var response = await RecoveryEndpointHarness.ResetAsync(
            client, resetToken, oversized, oversized);

        // then: 400; errors.password = ['Пароль — не более 128 символов'] —
        // единственная ошибка, состав не проверяется (ISS-016); Δkdf=0; токен жив.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.InvalidData, await RecoveryEndpointHarness.MessageAsync(response));
        var errors = await RecoveryEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            [ValidationTexts.PasswordMax],
            RecoveryEndpointHarness.ErrorOf(errors, "password"));
        Assert.Equal(
            0,
            RecoveryEndpointHarness.KdfDelta(before, RecoveryEndpointHarness.KdfSnapshot(_factory)));
        Assert.NotNull(RecoveryEndpointHarness.ResetTokenRecord(_factory, resetToken));
    }

    [Fact]
    public async Task Reset_ConfirmMismatch_400Mismatch_TokenLive()
    {
        // given: живой reset-токен.
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-reset-mismatch");
        var resetToken = RecoveryEndpointHarness.SeedResetToken(_factory, user.Id);
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);

        // when: повтор пароля не совпадает.
        using var response = await RecoveryEndpointHarness.ResetAsync(
            client, resetToken, RecoveryEndpointHarness.NewPassword, "Other1!x");

        // then: 400; errors.confirmPassword дословно password.mismatch; токен жив.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.InvalidData, await RecoveryEndpointHarness.MessageAsync(response));
        var errors = await RecoveryEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            [ValidationTexts.PasswordMismatch],
            RecoveryEndpointHarness.ErrorOf(errors, "confirmPassword"));
        Assert.NotNull(RecoveryEndpointHarness.ResetTokenRecord(_factory, resetToken));
    }

    [Fact]
    public async Task Reset_EmptyConfirmPassword_400Required_TokenLive()
    {
        // given: живой reset-токен; база Δkdf.
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-reset-empty-confirm");
        var resetToken = RecoveryEndpointHarness.SeedResetToken(_factory, user.Id);
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        var before = RecoveryEndpointHarness.KdfSnapshot(_factory);

        // when: confirmPassword пуст (обязательность поля — required, FR-006).
        using var response = await RecoveryEndpointHarness.ResetAsync(
            client, resetToken, RecoveryEndpointHarness.NewPassword, "  ");

        // then: 400 «Данные заполнены неверно» + errors.confirmPassword дословно
        // required; Δkdf=0; токен жив (полевая ошибка не гасит).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.InvalidData, await RecoveryEndpointHarness.MessageAsync(response));
        var errors = await RecoveryEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            [ValidationTexts.Required],
            RecoveryEndpointHarness.ErrorOf(errors, "confirmPassword"));
        Assert.Equal(
            0,
            RecoveryEndpointHarness.KdfDelta(before, RecoveryEndpointHarness.KdfSnapshot(_factory)));
        Assert.NotNull(RecoveryEndpointHarness.ResetTokenRecord(_factory, resetToken));
    }

    [Fact]
    public async Task Reset_MalformedJson_400WithoutErrors_TokenLive()
    {
        // given: живой reset-токен; база Δkdf.
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-reset-broken");
        var resetToken = RecoveryEndpointHarness.SeedResetToken(_factory, user.Id);
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        var before = RecoveryEndpointHarness.KdfSnapshot(_factory);

        // when: синтаксически некорректный JSON тела.
        using var response = await client.PostAsync(
            RecoveryEndpointHarness.ResetEndpoint,
            RecoveryEndpointHarness.Json("""{"resetToken": """));

        // then: 400 «Данные заполнены неверно» БЕЗ errors; Δkdf=0; токен жив.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.InvalidData, await RecoveryEndpointHarness.MessageAsync(response));
        using var body = await RecoveryEndpointHarness.ReadJsonAsync(response);
        Assert.False(body.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(
            0,
            RecoveryEndpointHarness.KdfDelta(before, RecoveryEndpointHarness.KdfSnapshot(_factory)));
        Assert.NotNull(RecoveryEndpointHarness.ResetTokenRecord(_factory, resetToken));
    }
}

/// <summary>
/// Warning-факты IF-016/ADR-034 для reset-password (контракт C-006): успех (204) —
/// РОВНО две записи «Api.Security» (факт «Сброс пароля» + отзыв refresh с
/// reason=password_reset), обе Warning и с traceId; ветки отказа (невалидный
/// токен, полевая ошибка) — ни одной записи (запись только при фактическом
/// выполнении операции). Маркерная проверка NFR-006: пароли, значения refresh- и
/// reset-токенов не встречаются ни в одной записи sink. Продюсеры записей —
/// SecurityEventLogger (переиспользованы хелперы SecurityEventLoggingHarness).
/// </summary>
public sealed class ResetPasswordSecurityLogTests(RecoveryApiFixture fixture) : IClassFixture<RecoveryApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Reset_Success_LogsPasswordResetAndRevocationFacts_WithoutSecrets()
    {
        // given: пользователь; refresh-токены двух «устройств» (значения известны —
        // поверхность маркерной проверки); живой reset-токен через request → confirm.
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-sec-reset");
        var firstDevice = RecoveryEndpointHarness.SeedRefreshToken(_factory, user.Id);
        var secondDevice = RecoveryEndpointHarness.SeedRefreshToken(_factory, user.Id);
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);
        var code = RecoveryEndpointHarness.LastEmailedCode(_factory);
        using var confirm = await RecoveryEndpointHarness.ConfirmAsync(client, user.Email, code);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        using var confirmBody = await RecoveryEndpointHarness.ReadJsonAsync(confirm);
        var resetToken = confirmBody.RootElement.GetProperty("resetToken").GetString();
        Assert.False(string.IsNullOrEmpty(resetToken));

        // given: чистый log-sink вокруг измеряемого сброса.
        _factory.LogSink.Clear();

        // when: успешный сброс пароля — 204.
        using var response = await RecoveryEndpointHarness.ResetAsync(
            client, resetToken!, RecoveryEndpointHarness.NewPassword, RecoveryEndpointHarness.NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // then: РОВНО две записи «Api.Security», обе Warning: факт «Сброс пароля»
        // и отзыв refresh с reason=password_reset; обе с traceId (анонимный вызов —
        // без subject).
        var records = SecurityEventLoggingHarness.SecurityRecords(_factory);
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal(LogLevel.Warning, record.Level));
        Assert.Single(records, record =>
            record.State.TryGetValue("Fact", out var fact) && fact as string == "Сброс пароля");
        Assert.Single(records, record =>
            record.State.TryGetValue("Reason", out var reason) &&
            reason as string == SecurityEventReasons.PasswordReset);
        Assert.All(records, record => Assert.False(
            string.IsNullOrWhiteSpace((string)record.State["TraceId"]!),
            "Запись события безопасности обязана нести traceId."));

        // then (NFR-006): маркеры секретов не встречаются НИ В ОДНОЙ записи sink:
        // ни старый/новый пароль, ни значения refresh-токенов, ни reset-токен.
        foreach (var record in _factory.LogSink.Snapshot())
        {
            var serialized = SecurityEventLoggingHarness.SerializeForMarkerCheck(record);
            Assert.DoesNotContain(RecoveryEndpointHarness.TestUserPassword, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(RecoveryEndpointHarness.NewPassword, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(firstDevice, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(secondDevice, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(resetToken!, serialized, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Reset_FailureBranches_WriteNoSecurityRecords()
    {
        // given: пользователь; живой reset-токен (останется живым при полевой ошибке).
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-sec-reset-bad");
        var resetToken = RecoveryEndpointHarness.SeedResetToken(_factory, user.Id);
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);

        // when №1: несуществующий токен → 400; чистый sink вокруг обеих попыток.
        _factory.LogSink.Clear();
        using var unknown = await RecoveryEndpointHarness.ResetAsync(
            client, "garbage", RecoveryEndpointHarness.NewPassword, RecoveryEndpointHarness.NewPassword);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        // when №2: полевая ошибка (слабый пароль) → 400.
        using var weak = await RecoveryEndpointHarness.ResetAsync(client, resetToken, "abc", "abc");
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        // then: ни одна ветка отказа не пишет события безопасности — запись только
        // при ФАКТИЧЕСКОМ выполнении операции (IF-016); токен остался жив.
        Assert.Empty(SecurityEventLoggingHarness.SecurityRecords(_factory));
        Assert.NotNull(RecoveryEndpointHarness.ResetTokenRecord(_factory, resetToken));
    }
}
