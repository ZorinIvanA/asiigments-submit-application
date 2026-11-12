using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-018 «Матрица политик: ключи/окна/лимиты» (happy_path, FR-004, P1).
///
/// given: реализация политик лимитера, зарегистрированная в DI.
/// when:  чтение конфигурации политик в тесте (+ поведенческая проба ключа login).
/// then:  login: ключ 'lower(trim(login))|IP', окно 60 с, лимит 5; register:
///        ключ 'IP', окно 3600 с, лимит 5; recovery_request: ключ
///        'lower(trim(email))', окно 3600 с, лимит 3 (FR-004 AC «Матрица:
///        ключи/окна/лимиты»).
/// </summary>
public sealed class Ts018_RateLimitPolicyMatrixTests : IClassFixture<B09WebAppFactory>
{
    private const string TestIp = "10.0.0.18";

    private readonly B09WebAppFactory _factory;

    public Ts018_RateLimitPolicyMatrixTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public void PolicyWindowsAndLimits_MatchMatrix()
    {
        // given/when: политики из DI тестового хоста (разрешение = регистрация в DI).
        var register = _factory.Services.GetRequiredService<RegisterLimiter>();
        var login = _factory.Services.GetRequiredService<LoginFailureLimiter>();
        var recovery = _factory.Services.GetRequiredService<RecoveryRequestLimiter>();
        Assert.NotNull(register);
        Assert.NotNull(login);
        Assert.NotNull(recovery);

        // then: матрица FR-004 — окна и лимиты.
        Assert.Equal(60_000L, LoginFailureLimiter.FailureWindowMs);
        Assert.Equal(5, LoginFailureLimiter.FailureLimit);
        Assert.Equal(3_600_000L, RegisterLimiter.RegisterWindowMs);
        Assert.Equal(5, RegisterLimiter.RegisterLimit);
        Assert.Equal(3_600_000L, RecoveryRequestLimiter.RequestWindowMs);
        Assert.Equal(3, RecoveryRequestLimiter.RequestLimit);
    }

    [Fact]
    public void PolicyKeys_FollowLowerTrimNormalization()
    {
        // then: ключи по матрице — lower(trim(...)) для login/email, trim для IP
        // (правило коллации ci; null/отсутствие → '').
        Assert.Equal("ivan", LimiterKeys.FromLogin("  Ivan "));
        Assert.Equal("ivan", LimiterKeys.FromLogin("IVAN"));
        Assert.Equal(string.Empty, LimiterKeys.FromLogin(null));
        Assert.Equal("10.0.0.1", LimiterKeys.FromIp(" 10.0.0.1 "));
        Assert.Equal(string.Empty, LimiterKeys.FromIp(null));
        Assert.Equal("st@example.com", LimiterKeys.FromEmail(" St@Example.COM "));
        Assert.Equal(string.Empty, LimiterKeys.FromEmail(null));
    }

    [Fact]
    public void LoginKey_CombinesNormalizedLoginAndIp()
    {
        // given: 5 учтённых неудачных входов для '  Ivan ' с IP (заполняют ключ
        // 'lower(trim(login))|IP' — 60-секундное окно, лимит 5).
        var login = _factory.Services.GetRequiredService<LoginFailureLimiter>();
        for (var attempt = 0; attempt < LoginFailureLimiter.FailureLimit; attempt++)
        {
            login.RegisterFailure("  Ivan ", TestIp);
        }

        // then: ключ блокируется при обращении с тем же нормализованным логином и IP.
        Assert.True(login.ShouldBlock("ivan", TestIp), "Исчерпанный ключ lower(trim(login))|IP должен блокировать.");
        Assert.True(login.ShouldBlock(" Ivan ", TestIp), "Нормализация ключа идемпотентна: ' Ivan ' === 'ivan'.");

        // then: другой IP или другой логин — ДРУГОЙ ключ (составной ключ login|IP).
        Assert.False(login.ShouldBlock("ivan", "10.0.0.181"), "Другой IP образует другой ключ.");
        Assert.False(login.ShouldBlock("  IVAN2 ", TestIp), "Другой логин образует другой ключ.");
    }
}
