using System.Net;
using System.Text.RegularExpressions;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Observability;
using LabsApp.Storage;
using LabsApp.Tests.Hosting;
using LabsApp.Tests.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LabsApp.Tests.Recovery;

// ============================================================================
// Юнит-тесты BUG-001 (TS-064, отдельный прогон): сбой IEmailSender в
// recovery/request НЕ меняет HTTP-ответ (IF-005: «ошибки отправки не меняют
// HTTP-ответ recovery/request — всегда 200 пустое тело»). Хост — TestWebAppFactory
// с подменой IEmailSender через WithWebHostBuilder (регистрация коллбэка
// выполняется после Program — выигрывает последняя, образец зон Auth/Hosting):
// DI-замена на падающую заглушку. Генерация и хранение кода, запись Error
// без секретов (NFR-006) и Δkdf=0 проверяются на том же хосте.
// ============================================================================

/// <summary>
/// Фикстура хоста с падающей заглушкой IEmailSender: производная фабрика
/// WithWebHostBuilder — собственный log-sink и DI-замена IEmailSender
/// (последняя регистрация выигрывает). Состояние изолировано от корневой
/// фабрики: сид и снимки KDF — только через <see cref="Host"/>.Services.
/// </summary>
public sealed class RecoveryFailingEmailSenderFixture : IDisposable
{
    /// <summary>Падающая заглушка доставки — единственная реализация IEmailSender хоста.</summary>
    public ThrowingRecoveryEmailSender Sender { get; } = new();

    /// <summary>Log-sink производного хоста (записи всех категорий).</summary>
    public TestLogSink LogSink { get; } = new();

    private readonly TestWebAppFactory _root = new(
        null,
        new Dictionary<string, string?>
        {
            [SeedOptions.DemoDataVariable] = "false",
            [AuthOptions.Pbkdf2IterationsVariable] = "1000",
        });

    /// <summary>Хост с подменённым IEmailSender (запросы и DI-сид — только он).</summary>
    public WebApplicationFactory<Program> Host { get; }

    public RecoveryFailingEmailSenderFixture()
    {
        Host = _root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(LogSink));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Sender);
            });
        });
    }

    public void Dispose()
    {
        Host.Dispose();
        _root.Dispose();
    }
}

/// <summary>
/// Падающая заглушка IEmailSender (канал доставки недоступен): захватывает
/// аргументы вызова до броска — тест узнаёт сгенерированный код для проверки
/// «в логе без секретов». Потокобезопасна.
/// </summary>
public sealed class ThrowingRecoveryEmailSender : IEmailSender
{
    private readonly object _gate = new();
    private readonly List<string[]> _captured = [];

    private int _calls;

    /// <summary>Число вызовов SendAsync (вызов с исключением тоже считается).</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Аргументы вызовов [toEmail, subject, body] до броска исключения.</summary>
    public IReadOnlyList<string[]> Captured
    {
        get
        {
            lock (_gate)
            {
                return _captured.ToArray();
            }
        }
    }

    /// <summary>Сброс счётчика и захваченных аргументов (изоляция тестов класса).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _captured.Clear();
            _calls = 0;
        }
    }

    public Task SendAsync(string toEmail, string subject, string body)
    {
        Interlocked.Increment(ref _calls);
        lock (_gate)
        {
            _captured.Add([toEmail, subject, body]);
        }

        throw new InvalidOperationException(
            "BUG-001: канал доставки email недоступен (падающая тестовая заглушка IEmailSender).");
    }
}

/// <summary>
/// FR-012/IF-005 (BUG-001): recovery/request при падающем IEmailSender —
/// 200 с пустым телом, Error-запись в журнале без секретов, код создан и
/// хранится, Δkdf=0.
/// </summary>
public sealed class RecoveryRequestFailingEmailSenderTests(RecoveryFailingEmailSenderFixture fixture)
    : IClassFixture<RecoveryFailingEmailSenderFixture>
{
    private readonly RecoveryFailingEmailSenderFixture _fixture = fixture;

    [Fact]
    public async Task Request_FailingEmailSender_Still200WithEmptyBodyAndSenderCalledOnce()
    {
        // given: зарегистрированный пользователь; чистая заглушка и log-sink.
        var user = SeedUser("rec-fail-body");
        _fixture.Sender.Reset();
        _fixture.LogSink.Clear();
        using var client = CreateClient();

        // when: запрос кода при падающем канале доставки.
        using var response = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);

        // then: 200 с ПУСТЫМ телом — 0 байт, Content-Length: 0, НЕ '{}' (ISS-014);
        // сбой доставки ответ не меняет (IF-005).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Content?.Headers.ContentLength == 0,
            $"Ожидался Content-Length: 0, фактически {response.Content?.Headers.ContentLength}.");
        Assert.Empty(await response.Content!.ReadAsByteArrayAsync());

        // then: заглушка вызвана ровно один раз с адресом запроса.
        Assert.Equal(1, _fixture.Sender.Calls);
        var captured = Assert.Single(_fixture.Sender.Captured);
        Assert.Equal(user.Email, captured[0]);
    }

    [Fact]
    public async Task Request_FailingEmailSender_CodeStillGeneratedStoredAndVerifiable()
    {
        // given: зарегистрированный пользователь; чистая заглушка.
        var user = SeedUser("rec-fail-code");
        _fixture.Sender.Reset();

        // when: запрос кода при падающем канале доставки.
        using (var client = CreateClient())
        {
            using var response = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // then: код создан и жив (сбой доставки хранение не отменяет):
        // attempts=0, usedAt=null, TTL 10 минут; значение кода (из захваченных
        // аргументов заглушки) верифицируется по хэшу хранилища.
        var live = _fixture.Host.Services.GetRequiredService<ISecurityTokenRepository>()
            .FindLiveForUser(user.Id);
        Assert.NotNull(live);
        Assert.Equal(0, live!.Attempts);
        Assert.Null(live.UsedAt);
        Assert.Equal(TimeSpan.FromMinutes(10), live.ExpiresAt - live.CreatedAt);

        var capturedBody = Assert.Single(_fixture.Sender.Captured)[2];
        var tokens = _fixture.Host.Services.GetRequiredService<ITokenService>();
        var code = Assert.Single(
            Regex.Matches(capturedBody, "[0-9]{6}").Select(match => match.Value),
            candidate => tokens.VerifyRecoveryCode(candidate, live.CodeHash));
        Assert.Matches("^[0-9]{6}$", code);
    }

    [Fact]
    public async Task Request_FailingEmailSender_LogsErrorWithoutSecretsAndZeroKdf()
    {
        // given: зарегистрированный пользователь; база log-sink и Δkdf.
        var user = SeedUser("rec-fail-log");
        _fixture.Sender.Reset();
        _fixture.LogSink.Clear();
        var kdf = _fixture.Host.Services.GetRequiredService<IKdfCounter>();
        var before = kdf.Snapshot();
        using var client = CreateClient();

        // when: запрос кода при падающем канале доставки.
        using var response = await RecoveryEndpointHarness.RequestCodeAsync(client, user.Email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // then: ошибка зафиксирована НОВОЙ записью уровня Error...
        var newEntries = _fixture.LogSink.Snapshot();
        Assert.Contains(newEntries, entry => entry.Level == LogLevel.Error);

        // ...и БЕЗ секретов (NFR-006): код восстановления не попал ни в сообщение,
        // ни в исключение ни одной записи журнала.
        var capturedBody = Assert.Single(_fixture.Sender.Captured)[2];
        var tokens = _fixture.Host.Services.GetRequiredService<ITokenService>();
        var live = _fixture.Host.Services.GetRequiredService<ISecurityTokenRepository>()
            .FindLiveForUser(user.Id);
        Assert.NotNull(live);
        var code = Assert.Single(
            Regex.Matches(capturedBody, "[0-9]{6}").Select(match => match.Value),
            candidate => tokens.VerifyRecoveryCode(candidate, live!.CodeHash));
        foreach (var entry in _fixture.LogSink.Snapshot())
        {
            Assert.DoesNotContain(code, entry.Message, StringComparison.Ordinal);
            Assert.True(
                entry.Exception is null || !entry.Exception.Message.Contains(code, StringComparison.Ordinal),
                $"Код восстановления обнаружен в исключении записи «{entry.Category}» (NFR-006).");
        }

        // then: Δkdf=0 — сбой доставки не порождает дериваций (FR-004/ASM-005).
        var after = kdf.Snapshot();
        Assert.Equal(
            0,
            after.Values.Sum() - before.Values.Sum());
    }

    private HttpClient CreateClient() =>
        _fixture.Host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>DI-сид пользователя с реальным PBKDF2-хэшем в хост с падающей заглушкой.</summary>
    private User SeedUser(string login)
    {
        var services = _fixture.Host.Services;
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
}
