using System.Diagnostics.Metrics;
using LabsApp.Auth.RateLimiting;
using LabsApp.Hosting.Configuration;
using LabsApp.Observability;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace LabsApp.Auth;

/// <summary>
/// Регистрации Api.Auth.Core (C-004): KDF-сервис и счётчик дериваций IF-002,
/// токены и хэши кодов IF-003, cookie IF-004, лимитеры IF-006 (потолок ключей —
/// константа движка FR-003) и собственная схема аутентификации ADR-003 со
/// схемой по умолчанию. Вызывается одной строкой из Program.cs; позиция
/// UseAuthentication в конвейере не меняется (якорь C-001).
/// Все реализации — singleton (состояние процесса: лимитеры и токены
/// сбрасываются перезапуском). Эталонный хэш пароля для VerifyReference
/// создаётся при конструировании хэшера (ровно одна деривация, метка
/// reference) и прогревается при старте хоста (<see cref="AuthCoreWarmup"/>).
/// </summary>
public static class AuthCoreServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует модуль Api.Auth.Core. Для изолированных ServiceCollection
    /// (юнит-тесты) подсматривает умолчания TimeProvider/Meter/StorageLock через
    /// TryAdd — в хосте раньше зарегистрированные реализации выигрывают.
    /// </summary>
    public static IServiceCollection AddAuthCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(_ => new Meter(ObservabilityMiddleware.MeterName, "1.0"));
        services.TryAddSingleton<StorageLock>();
        // Умолчание окружения только для изолированных ServiceCollection
        // (юнит-тесты): в хосте IHostEnvironment уже зарегистрирован — TryAdd
        // не срабатывает, признак Development берётся из реального окружения.
        services.TryAddSingleton<IHostEnvironment>(_ => new HostingEnvironment
        {
            ApplicationName = "LabsApp",
            EnvironmentName = Environments.Development,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // IF-002: счётчик дериваций (метрика + лог раз в 60 с) и KDF-сервис
        // (итерации Auth__Pbkdf2Iterations; эталонная деривация — метка reference).
        services.AddSingleton<IKdfCounter, KdfCounter>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // IF-003: access-JWT HS256, opaque refresh/reset (SHA-256 при хранении),
        // коды восстановления и их солёный SHA-256 (0 KDF на recovery — ASM-005).
        services.AddSingleton<ITokenService, TokenService>();

        // IF-015/T-101 (FR-024): консолидированное in-memory хранилище refresh-
        // токенов, кодов восстановления и reset-токенов — singleton под общим
        // StorageLock (выше TryAdd; в хосте выигрывает регистрация AddInMemoryStorage).
        // Регистрации токенных хранилищ собраны в AddAuthCore — единый
        // писатель DI-регистраций Auth (ADR-030).
        services.AddSingleton<ISecurityTokenRepository, InMemorySecurityTokenRepository>();

        // IF-004: cookie — обе Path=/, Secure iff окружение ≠ Development.
        services.AddSingleton<ICookieService, CookieService>();

        // IF-006: IP — только Connection.RemoteIpAddress (ADR-006) + состояние
        // лимитеров (IF-015/FR-024, ADR-026 — объявлено в зоне Auth/RateLimiting;
        // изоляция лимитеров — по именам политик RateLimitPolicies) + прикладные
        // лимитеры по матрице FR-004 — каждый со собственным движком/политикой.
        services.TryAddSingleton<IRateLimitStore, InMemoryRateLimitStore>();
        services.AddSingleton<IClientIpResolver, ClientIpResolver>();
        services.AddSingleton<RegisterLimiter>();
        services.AddSingleton<LoginFailureLimiter>();
        services.AddSingleton<RecoveryRequestLimiter>();

        // Прогрев: эталонный хэш пароля готов ДО первого запроса (ровно одна
        // деривация на старт, метка reference — AR-004/SEC-001).
        services.AddHostedService<AuthCoreWarmup>();

        // ADR-003: собственная схема по cookie access_token с детерминированным 401.
        services
            .AddAuthentication(AuthCoreDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, CookieAuthenticationHandler>(
                AuthCoreDefaults.AuthenticationScheme, static _ => { });

        return services;
    }

    /// <summary>
    /// Eager-инициализация KDF-хэшера при старте хоста: конструктор-инъекция
    /// IPasswordHasher выполняет единственную эталонную деривацию (метка
    /// reference) при конструировании hosted-сервиса — до начала обслуживания
    /// запросов; первый запрос ветки отказа не платит за инициализацию эталона.
    /// </summary>
    internal sealed class AuthCoreWarmup(IPasswordHasher hasher) : IHostedService
    {
        private readonly IPasswordHasher _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Эталонный хэш уже вычислен конструктором singleton'а (инъекция
            // выше); явное касание фиксирует готовность на старте хоста.
            _ = _hasher;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
