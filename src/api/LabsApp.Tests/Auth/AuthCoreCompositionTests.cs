using LabsApp.Auth;
using LabsApp.Auth.RateLimiting;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.Tests.Auth;

/// <summary>
/// Юнит-проверки DI-модуля AddAuthCore (якорь Program.cs): все сервисы C-004
/// разрешаются (KDF-хэшер и счётчик, токены, cookie, лимитеры), схема
/// аутентификации зарегистрирована и является схемой по умолчанию,
/// hosted-прогрев KDF-хэшера присутствует.
/// </summary>
public sealed class AuthCoreCompositionTests
{
    [Fact]
    public void AddAuthCore_ResolvesAllModuleServices()
    {
        using var provider = new ServiceCollection()
            .AddAuthCore()
            .Configure<AuthOptions>(options => options.JwtKey = "composition-test-key-0123456789abcdef-0123456789abcdef")
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IKdfCounter>());
        Assert.NotNull(provider.GetRequiredService<IPasswordHasher>());
        Assert.NotNull(provider.GetRequiredService<ITokenService>());
        Assert.NotNull(provider.GetRequiredService<ICookieService>());
        Assert.NotNull(provider.GetRequiredService<IClientIpResolver>());
        Assert.NotNull(provider.GetRequiredService<IRateLimitStore>());
        Assert.NotNull(provider.GetRequiredService<RegisterLimiter>());
        Assert.NotNull(provider.GetRequiredService<LoginFailureLimiter>());
        Assert.NotNull(provider.GetRequiredService<RecoveryRequestLimiter>());

        // T-101 (IF-015/FR-024): консолидированное хранилище токенов безопасности
        // (refresh/reset/коды восстановления) регистрируется AddAuthCore как
        // InMemorySecurityTokenRepository (AC T-101 «DI-резолв»).
        Assert.IsType<InMemorySecurityTokenRepository>(provider.GetRequiredService<ISecurityTokenRepository>());

        // Синглтоны (состояние процесса — сброс перезапуском).
        Assert.Same(provider.GetRequiredService<IKdfCounter>(), provider.GetRequiredService<IKdfCounter>());
        Assert.Same(provider.GetRequiredService<IPasswordHasher>(), provider.GetRequiredService<IPasswordHasher>());
        Assert.Same(provider.GetRequiredService<ITokenService>(), provider.GetRequiredService<ITokenService>());
        Assert.Same(provider.GetRequiredService<RecoveryRequestLimiter>(), provider.GetRequiredService<RecoveryRequestLimiter>());
        Assert.Same(
            provider.GetRequiredService<ISecurityTokenRepository>(),
            provider.GetRequiredService<ISecurityTokenRepository>());
    }

    [Fact]
    public void AddAuthCore_LimitersShareStore_ByDistinctPolicies()
    {
        // IF-006/IF-015 (FR-024, ADR-026): состояние лимитеров — за интерфейсом
        // IRateLimitStore; прикладные лимитеры делят общий singleton-экземпляр,
        // изоляция — по именам политик RateLimitPolicies (матрица FR-004).
        using var provider = new ServiceCollection()
            .AddAuthCore()
            .BuildServiceProvider();

        var store = provider.GetRequiredService<IRateLimitStore>();

        provider.GetRequiredService<RegisterLimiter>().TryAcquire("10.0.0.1");
        provider.GetRequiredService<LoginFailureLimiter>().RegisterFailure("teacher", "10.0.0.1");
        provider.GetRequiredService<RecoveryRequestLimiter>().TryAcquire("a@b.ru");

        Assert.Equal(1, store.TrackedKeysCount(RateLimitPolicies.Register));
        Assert.Equal(1, store.TrackedKeysCount(RateLimitPolicies.Login));
        Assert.Equal(1, store.TrackedKeysCount(RateLimitPolicies.RecoveryRequest));
    }

    [Fact]
    public void AddAuthCore_HasherAndCounterAreWired()
    {
        using var provider = new ServiceCollection()
            .AddAuthCore()
            .Configure<AuthOptions>(options =>
            {
                options.JwtKey = "composition-test-key-0123456789abcdef-0123456789abcdef";
                options.Pbkdf2Iterations = 1000;
            })
            .BuildServiceProvider();

        var hasher = provider.GetRequiredService<IPasswordHasher>();
        var counter = provider.GetRequiredService<IKdfCounter>();

        // Эталонная деривация при старте учтена с меткой reference.
        var snapshot = counter.Snapshot();
        Assert.Equal(1, snapshot[KdfCallers.Reference]);

        var hash = hasher.Hash("pwd", KdfCallers.Seed);
        Assert.True(hasher.Verify("pwd", hash, KdfCallers.Login));
        Assert.False(hasher.VerifyReference("anything"));

        var after = counter.Snapshot();
        Assert.Equal(1, after[KdfCallers.Seed]);
        Assert.Equal(1, after[KdfCallers.Login]);
        Assert.Equal(2, after[KdfCallers.Reference]);
    }

    [Fact]
    public async Task AddAuthCore_RegistersDefaultAuthenticationScheme()
    {
        using var provider = new ServiceCollection()
            .AddAuthCore()
            .BuildServiceProvider();

        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.NotNull(await schemes.GetSchemeAsync(AuthCoreDefaults.AuthenticationScheme));

        var defaultScheme = await schemes.GetDefaultAuthenticateSchemeAsync();
        Assert.Equal(AuthCoreDefaults.AuthenticationScheme, defaultScheme?.Name);
    }

    [Fact]
    public void AddAuthCore_RegistersHasherWarmupHostedService()
    {
        using var provider = new ServiceCollection()
            .AddAuthCore()
            .Configure<AuthOptions>(options => options.Pbkdf2Iterations = 1000)
            .BuildServiceProvider();

        // Hosted-прогрев: эталонная деривация выполняется при старте хоста,
        // до обслуживания запросов (AR-004/SEC-001).
        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
        Assert.Contains(hostedServices, service => service.GetType().Name == "AuthCoreWarmup");
    }
}
