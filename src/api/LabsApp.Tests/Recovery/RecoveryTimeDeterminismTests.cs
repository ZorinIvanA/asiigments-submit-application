using System.Net;
using System.Text.RegularExpressions;
using LabsApp.Auth;
using LabsApp.Auth.RateLimiting;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Observability;
using LabsApp.Storage;
using LabsApp.Tests.Hosting;
using LabsApp.Tests.Observability;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ValidationTexts = LabsApp.Domain.Validation.ErrorTexts;

namespace LabsApp.Tests.Recovery;

// ============================================================================
// Детерминированное время recovery-эндпойнтов (C-006, IF-008; FR-012/FR-013):
// TTL кода (10 минут), гашение переотправкой, TTL reset-токена (15 минут) и
// окно лимитера recovery_request (3/3600с) проверяются ПО FakeTimeProvider
// (ADR-002, образец AuthApiFactory): RegisterLimiter-подобное окно кода не
// зависит от настенных часов. RecoveryEndpointTests закрывает сценарии AC по
// составу ответов; здесь — ТОЛЬКО временные границы вплоть до равенства
// (expiresAt == now → код не жив; метка ровно windowMs назад — вне окна).
// Δkdf request+confirm = 0 гейтится тем же прогоном (FR-004/ASM-005).
// ============================================================================

/// <summary>
/// Фикстура recovery-хоста с подменой TimeProvider на <see cref="FakeTimeProvider"/>
/// (образец AuthApiFactory): регистрация добавляется позже регистраций Program —
/// выигрывает последняя; вотчеры конфигурации отключены (пер-пользовательский
/// лимит inotify-экземпляров, образец зон Auth/B-03). Log-sink общий с корневой
/// фабрикой (провайдер регистрируется ConfigureWebHost корня).
/// </summary>
public sealed class RecoveryFakeTimeFactory : IDisposable
{
    /// <summary>Единый источник бизнес-времени тестового хоста (ADR-002).</summary>
    public FakeTimeProvider Clock { get; } = new();

    private readonly TestWebAppFactory _root = new(
        null,
        new Dictionary<string, string?>
        {
            [SeedOptions.DemoDataVariable] = "false",
            [AuthOptions.Pbkdf2IterationsVariable] = "1000",
        });

    private WebApplicationFactory<Program>? _factory;

    /// <summary>Log-sink хоста (изоляция тестов — Clear() в теле теста).</summary>
    public TestLogSink LogSink => _root.LogSink;

    /// <summary>Хост с FakeTimeProvider: TTL кодов/токенов и окна лимитеров детерминированы.</summary>
    public WebApplicationFactory<Program> Host =>
        _factory ??= _root.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Clock));
        });

    /// <inheritdoc/>
    public void Dispose()
    {
        _factory?.Dispose();
        _root.Dispose();
    }
}

/// <summary>
/// FR-012/FR-013 по FakeTimeProvider: код жив РОВНО 10 минут (за секунду до
/// границы confirm проходит, на границе — уже нет), переотправка гасит прежний
/// код, reset-токен жив РОВНО 15 минут от момента confirm; 4-й запрос кода за
/// час отклоняется 429 одинаково для существующего и несуществующего email,
/// окно освобождается ровно через час. request+confirm — Δkdf=0.
/// </summary>
public sealed class RecoveryTimeDeterminismTests(RecoveryFakeTimeFactory fixture)
    : IClassFixture<RecoveryFakeTimeFactory>
{
    private readonly RecoveryFakeTimeFactory _fixture = fixture;

    [Fact]
    public async Task Request_CodeLivesExactlyTenMinutes_ConfirmSecondBeforeExpiry_200_ZeroKdf()
    {
        // given: пользователь; база Δkdf до request.
        var user = SeedUser("rec-time-ttl");
        using var client = CreateClient();
        _fixture.LogSink.Clear();
        var kdf = HostKdfCounter();
        var before = kdf.Snapshot();

        // when: запрос кода; сдвиг на 1 секунду ДО границы 10 минут.
        using var response = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await RecoveryEndpointHarness.AssertEmptyBodyAsync(response);

        var live = HostServices().GetRequiredService<ISecurityTokenRepository>()
            .FindLiveForUser(user.Id);
        Assert.NotNull(live);
        Assert.Null(live!.UsedAt);
        Assert.Equal(0, live.Attempts);
        Assert.Equal(TimeSpan.FromMinutes(10), live.ExpiresAt - live.CreatedAt);
        Assert.Equal(live.ExpiresAt, _fixture.Clock.GetUtcNow().UtcDateTime.AddMinutes(10));

        _fixture.Clock.Advance(TimeSpan.FromMinutes(10).Subtract(TimeSpan.FromSeconds(1)));
        var code = EmailedCode();

        // then: за секунду до истечения код ещё жив — confirm проходит (200).
        using var confirm = await RecoveryEndpointHarness.ConfirmAsync(client, user.Email, code);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        using var body = await RecoveryEndpointHarness.ReadJsonAsync(confirm);
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("resetToken").GetString()));

        // then: request + confirm — ровно 0 дериваций (FR-004/ASM-005).
        Assert.Equal(0, DeltaKdf(before, kdf.Snapshot()));
    }

    [Fact]
    public async Task Request_ExpiryBoundaryAtExactlyTenMinutes_CodeNotLive_Confirm400UnifiedText()
    {
        // given: живой код, полученный через request.
        var user = SeedUser("rec-time-boundary");
        using var client = CreateClient();
        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);
        var code = EmailedCode();

        // when: часы переведены РОВНО на границу expiresAt (expiresAt == now —
        // код не жив: живость требует expiresAt > now, IF-015).
        _fixture.Clock.Advance(TimeSpan.FromMinutes(10));

        // then: живых кодов нет; confirm ВЕРНЫМ значением просроченного кода —
        // единый 400 «Код восстановления не подходит» (FR-013).
        Assert.Null(
            HostServices().GetRequiredService<ISecurityTokenRepository>().FindLiveForUser(user.Id));
        using var response = await RecoveryEndpointHarness.ConfirmAsync(client, user.Email, code);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.RecoveryCodeRejected, await RecoveryEndpointHarness.MessageAsync(response));
    }

    [Fact]
    public async Task Resend_ExtinguishesPreviousCode_Old400_New200_ResetTokenTtlExactlyFifteenMinutes()
    {
        // given: код C1 в момент t0.
        var user = SeedUser("rec-time-resend");
        using var client = CreateClient();
        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);
        var firstCode = EmailedCode();

        // when: переотправка через 3 минуты.
        _fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        using var second = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondCode = EmailedCode();
        Assert.NotEqual(firstCode, secondCode);

        // then: прежний код погашен — confirm C1 → 400 (usedAt ≠ null после
        // переотправки, IF-015); новый жив.
        using var oldConfirm = await RecoveryEndpointHarness.ConfirmAsync(client, user.Email, firstCode);
        Assert.Equal(HttpStatusCode.BadRequest, oldConfirm.StatusCode);
        Assert.Equal(ValidationTexts.RecoveryCodeRejected, await RecoveryEndpointHarness.MessageAsync(oldConfirm));

        // then: confirm C2 → 200; reset-токен жив РОВНО 15 минут от момента
        // confirm (детерминированно по FakeTimeProvider, IF-003/ASM-002).
        using var newConfirm = await RecoveryEndpointHarness.ConfirmAsync(client, user.Email, secondCode);
        Assert.Equal(HttpStatusCode.OK, newConfirm.StatusCode);
        using var body = await RecoveryEndpointHarness.ReadJsonAsync(newConfirm);
        var resetToken = body.RootElement.GetProperty("resetToken").GetString();
        Assert.False(string.IsNullOrEmpty(resetToken));

        var record = HostServices().GetRequiredService<ISecurityTokenRepository>()
            .FindLiveResetByHash(Sha256Hex(resetToken!));
        Assert.NotNull(record);
        Assert.Equal(user.Id, record!.UserId);
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            record.ExpiresAt - _fixture.Clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task Limiter_FourthRequestExistingAndNonexistent_Identical429_WindowSlidesAfterExactlyOneHour()
    {
        // given: существующий email X и несуществующий Y; по 3 запроса на каждый.
        var user = SeedUser("rec-time-limiter");
        using var client = CreateClient();
        const string existing = "rec-time-x@example.com";
        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, existing);
        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, existing);
        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, existing);

        // when №1: 4-й запрос на СУЩЕСТВУЮЩИЙ email.
        using var fourthExisting = await RecoveryEndpointHarness.RequestCodeAsync(client, existing);

        // when №2: 3 запроса на несуществующий Y и 4-й на него.
        const string nonexistent = "rec-time-y@example.com";
        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, nonexistent);
        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, nonexistent);
        _ = await RecoveryEndpointHarness.RequestCodeAsync(client, nonexistent);
        using var fourthNonexistent = await RecoveryEndpointHarness.RequestCodeAsync(client, nonexistent);

        // then: оба — 429 с ОДИНАКОВЫМ статусом и сообщением (оракула нет, FR-004).
        Assert.Equal(HttpStatusCode.TooManyRequests, fourthExisting.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, fourthNonexistent.StatusCode);
        Assert.Equal(
            await RecoveryEndpointHarness.MessageAsync(fourthExisting),
            await RecoveryEndpointHarness.MessageAsync(fourthNonexistent));
        Assert.Equal(ValidationTexts.RateLimited, await RecoveryEndpointHarness.MessageAsync(fourthExisting));

        // when №3: окно сдвинуто РОВНО на 3600с — метки ровно windowMs назад вне
        // окна (IF-006), ключ освобождён.
        _fixture.Clock.Advance(TimeSpan.FromMilliseconds(RecoveryRequestLimiter.RequestWindowMs));

        // then: 5-й по счёту запрос на X допущен (200; счётчик попыток начат заново).
        using var afterWindow = await RecoveryEndpointHarness.RequestCodeAsync(client, existing);
        Assert.Equal(HttpStatusCode.OK, afterWindow.StatusCode);
        await RecoveryEndpointHarness.AssertEmptyBodyAsync(afterWindow);
    }

    // ------------------------------ Помощники ------------------------------

    private WebApplicationFactory<Program> Host => _fixture.Host;

    private IServiceProvider HostServices() => Host.Services;

    private HttpClient CreateClient() =>
        Host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    private IKdfCounter HostKdfCounter() => HostServices().GetRequiredService<IKdfCounter>();

    private static long DeltaKdf(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after) =>
        after.Values.Sum() - before.Values.Sum();

    /// <summary>DI-сид пользователя с реальным PBKDF2-хэшем (до снимков Δkdf).</summary>
    private User SeedUser(string login)
    {
        var services = HostServices();
        var passwordHash = services.GetRequiredService<IPasswordHasher>()
            .Hash(RecoveryEndpointHarness.TestUserPassword, KdfCallers.Seed);
        var repository = services.GetRequiredService<IUserRepository>();
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

    /// <summary>Код из ПОСЛЕДНЕЙ записи «EmailDev» (маркер [DEV-EMAIL], ровно один 6-значный).</summary>
    private string EmailedCode()
    {
        var records = _fixture.LogSink.Snapshot()
            .Where(record => record.Category == DevEmailSender.LogCategory)
            .ToList();
        Assert.True(records.Count > 0, "Не найдено записей категории «EmailDev».");
        Assert.Contains(DevEmailSender.DevEmailMarker, records[^1].Message, StringComparison.Ordinal);

        return Assert.Single(
            Regex.Matches(records[^1].Message, "[0-9]{6}").Select(match => match.Value).Distinct());
    }

    /// <summary>SHA-256 hex значения reset-токена — зеркало TokenService (IF-003).</summary>
    private static string Sha256Hex(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
