using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B08.Limiters.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-018 «Матрица политик лимитера: ключи/окна/лимиты» (happy_path,
/// FR-004, P0). Тестовый хост ApiFactory — фикстура
/// B08LimitersRateLimitWebAppFactory: три прикладных лимитера резолвятся из
/// DI хоста (регистрации AddAuthCore — IF-006), бизнес-время —
/// FakeTimeProvider (шов конфигурации движка). Зарегистрированные параметры
/// политик читаются поведением публичного шва (RegisterFailure/ShouldBlock/
/// TryAcquire + сдвиг часов): лимит — число допусков до отказа/блокировки;
/// окно — допуск после сдвига часов на window+1 мс; ключ — совпадение
/// регистровых/trim-вариантов и различие прочих пар.
///
/// then:  login — ключ 'lower(trim(login))|IP', окно 60000 мс, лимит 5;
///        register — ключ 'IP', окно 3600000 мс, лимит 5; recovery_request —
///        ключ 'lower(trim(email))', окно 3600000 мс, лимит 3 (AC FR-004
///        «Матрица: ключи/окна/лимиты»).
/// </summary>
public sealed class Ts018_PolicyMatrixKeysWindowsLimitsTests : IClassFixture<B08LimitersRateLimitWebAppFactory>
{
    private const string ClientIp = "10.19.8.21";
    private const string OtherIp = "10.19.8.22";

    private readonly B08LimitersRateLimitWebAppFactory _factory;

    public Ts018_PolicyMatrixKeysWindowsLimitsTests(B08LimitersRateLimitWebAppFactory factory) => _factory = factory;

    [Fact]
    public void RegisteredPolicies_LoginRegisterRecovery_MatrixKeysWindowsLimits()
    {
        // when/then: чтение зарегистрированных параметров всех трёх политик.
        LoginPolicy_KeyLowerTrimLoginPipeIp_Window60000_Limit5();
        RegisterPolicy_KeyIp_Window3600000_Limit5();
        RecoveryRequestPolicy_KeyLowerTrimEmail_Window3600000_Limit3();
    }

    /// <summary>login: ключ 'lower(trim(login))|IP', окно 60000 мс, лимит 5.</summary>
    private void LoginPolicy_KeyLowerTrimLoginPipeIp_Window60000_Limit5()
    {
        var limiter = _factory.Services.GetRequiredService<LoginFailureLimiter>();

        // Лимит 5: 4 учтённой неудачи (регистрово/trim-варианты — один ключ)
        // ещё не блокируют, 5-я — блокирует.
        limiter.RegisterFailure("MatrixLogin", ClientIp);
        limiter.RegisterFailure(" matrixlogin ", ClientIp);
        limiter.RegisterFailure("MATRIXLOGIN", ClientIp);
        limiter.RegisterFailure("matrixLOGIN", ClientIp);
        Assert.False(
            limiter.ShouldBlock("matrixlogin", ClientIp),
            "login: 4 неудачи в одном ключе 'lower(trim(login))|IP' — блокировки ещё нет (лимит 5)");
        limiter.RegisterFailure("matrixlogin", ClientIp);
        Assert.True(
            limiter.ShouldBlock("matrixlogin", ClientIp),
            "login: 5-я неудача исчерпывает лимит 5");

        // Ключ 'lower(trim(login))|IP': другие IP и другие логины независимы.
        Assert.False(
            limiter.ShouldBlock("matrixlogin", OtherIp),
            "login: IP — часть ключа, другая пара (login, IP) не блокирована");
        Assert.False(
            limiter.ShouldBlock("otherlogin", ClientIp),
            "login: логин — часть ключа, другая пара (login, IP) не блокирована");

        // Окно 60000 мс: после сдвига часов на window+1 мс метки вычищены.
        _factory.Time.Advance(TimeSpan.FromMilliseconds(60_001));
        Assert.False(
            limiter.ShouldBlock("matrixlogin", ClientIp),
            "login: окно 60000 мс — после сдвига на 60001 мс блокировки нет");
    }

    /// <summary>register: ключ 'IP', окно 3600000 мс, лимит 5.</summary>
    private void RegisterPolicy_KeyIp_Window3600000_Limit5()
    {
        var limiter = _factory.Services.GetRequiredService<RegisterLimiter>();

        // Лимит 5: 5 допусков, 6-й — отказ.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.True(limiter.TryAcquire(ClientIp), $"register: допуск {attempt}");
        }

        Assert.False(limiter.TryAcquire(ClientIp), "register: 6-я попытка отклоняется (лимит 5)");

        // Ключ 'IP': другой IP обслуживается независимо (логин в ключ не входит).
        Assert.True(limiter.TryAcquire(OtherIp), "register: ключ — IP, другой IP не ограничен");

        // Окно 3600000 мс: после сдвига часов на window+1 мс метки вычищены.
        _factory.Time.Advance(TimeSpan.FromMilliseconds(3_600_001));
        Assert.True(
            limiter.TryAcquire(ClientIp),
            "register: окно 3600000 мс — после сдвига на 3600001 мс допуск снова возможен");
    }

    /// <summary>recovery_request: ключ 'lower(trim(email))', окно 3600000 мс, лимит 3.</summary>
    private void RecoveryRequestPolicy_KeyLowerTrimEmail_Window3600000_Limit3()
    {
        var limiter = _factory.Services.GetRequiredService<RecoveryRequestLimiter>();

        // Лимит 3: 3 допуска (регистрово/trim-варианты — один ключ), 4-й — отказ.
        Assert.True(limiter.TryAcquire("matrixuser@example.com"), "recovery: допуск 1");
        Assert.True(limiter.TryAcquire(" MATRIXUSER@example.com "), "recovery: допуск 2 (trim+ci — тот же ключ)");
        Assert.True(limiter.TryAcquire("MatrixUser@Example.com"), "recovery: допуск 3 (ci — тот же ключ)");
        Assert.False(
            limiter.TryAcquire("matrixuser@example.com"),
            "recovery: 4-я попытка отклоняется (лимит 3)");

        // Ключ 'lower(trim(email))': другой email обслуживается независимо.
        Assert.True(limiter.TryAcquire("other.user@example.com"), "recovery: другой email — другой ключ");

        // Окно 3600000 мс: после сдвига часов на window+1 мс метки вычищены.
        _factory.Time.Advance(TimeSpan.FromMilliseconds(3_600_001));
        Assert.True(
            limiter.TryAcquire("matrixuser@example.com"),
            "recovery: окно 3600000 мс — после сдвига на 3600001 мс допуск снова возможен");
    }
}
