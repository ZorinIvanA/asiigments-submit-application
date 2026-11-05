using LabsApp.Auth.RateLimiting;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-164 «Матрица лимитеров FR-004: прямая инспекция параметров политик
/// (ключи/окна/лимиты)» (happy_path, FR-004, P1).
///
/// given: публичные параметры прикладных лимитеров
///        src/api/LabsApp/Auth/RateLimiting/ApplicationRateLimiters.cs:
///        LoginFailureLimiter.FailureWindowMs=60000 / FailureLimit=5,
///        RegisterLimiter.RegisterWindowMs=3600000 / RegisterLimit=5,
///        RecoveryRequestLimiter.RequestWindowMs=3600000 / RequestLimit=3;
///        имена политик состояния — RateLimitPolicies.Login / Register /
///        RecoveryRequest; состав ключей: вход —
///        LimiterKeys.FromLogin(login)+'|'+LimiterKeys.FromIp(ip),
///        регистрация — LimiterKeys.FromIp(ip), recovery —
///        LimiterKeys.FromEmail(email); трим и нижний регистр — внутри
///        LimiterKeys.
/// when:  чтение значений констант и имён политик в тесте прямыми
///        типизированными ссылками (без магических строк); проверка
///        нормализации ключей вызовами LimiterKeys с пробельно-регистровыми
///        вариациями ('  Teacher ' → 'teacher', '  1.2.3.4 ' → '1.2.3.4',
///        ' Unknown@Example.com ' → 'unknown@example.com').
/// then:  login: окно 60000 мс, лимит 5, ключ 'lower(trim(login))|IP';
///        register: окно 3600000 мс, лимит 5, ключ IP (без логина);
///        recovery_request: окно 3600000 мс, лимит 3, ключ lower(trim(email)) —
///        матрица FR-004 AC «Матрица: ключи/окна/лимиты» подтверждается
///        прямыми значениями конфигурации политик, а не только поведенческой
///        блокировкой.
///
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-07 («ЕДИНСТВЕННАЯ зона исполнения кейса», файл Ts164*
/// создаётся при реализации владельцем; письмо в tests/integration/B-08
/// запрещено). В зоне tests/integration/B-09 идентификатор Ts164 свободен.
/// Поведенческая часть кейса исполнима дословно и исполнена в собственной зоне
/// батча B-09 (прецедент c-1052); расхождение размещения зафиксировано в
/// scenario_change_requests. Компиляция — часть проверки: ссылки на константы
/// типизированные, исчезновение члена контракта ломает сборку зоны.
/// </summary>
public sealed class Ts164_LimiterPolicyMatrixTests
{
    [Fact]
    public void LoginLimiter_MatrixConstants_KeyCompositionAndPolicyName()
    {
        // then: login — окно 60000 мс, лимит 5 (матрица FR-004: 5/60с).
        Assert.Equal(60_000, LoginFailureLimiter.FailureWindowMs);
        Assert.Equal(5, LoginFailureLimiter.FailureLimit);

        // then: имя политики состояния (без магических строк в потребителях).
        Assert.Equal("login", RateLimitPolicies.Login);

        // then: состав ключа входа 'lower(trim(login))|IP' — нормализация
        // внутри LimiterKeys, разделитель '|' (фактический BuildKey дерева).
        Assert.Equal("teacher|1.2.3.4",
            LimiterKeys.FromLogin("  Teacher ") + "|" + LimiterKeys.FromIp("  1.2.3.4 "));
        Assert.Equal("teacher", LimiterKeys.FromLogin("  Teacher "));
        Assert.Equal("1.2.3.4", LimiterKeys.FromIp("  1.2.3.4 "));
    }

    [Fact]
    public void RegisterLimiter_MatrixConstants_KeyIsIpOnlyAndPolicyName()
    {
        // then: register — окно 3600000 мс, лимит 5 (матрица FR-004: 5/3600с по IP).
        Assert.Equal(3_600_000, RegisterLimiter.RegisterWindowMs);
        Assert.Equal(5, RegisterLimiter.RegisterLimit);

        // then: имя политики состояния.
        Assert.Equal("register", RateLimitPolicies.Register);

        // then: ключ — IP (без логина): FromIp не добавляет login.
        Assert.Equal("1.2.3.4", LimiterKeys.FromIp(" 1.2.3.4 "));
    }

    [Fact]
    public void RecoveryRequestLimiter_MatrixConstants_KeyIsEmailAndPolicyName()
    {
        // then: recovery_request — окно 3600000 мс, лимит 3 (матрица FR-004: 3/3600с по email).
        Assert.Equal(3_600_000, RecoveryRequestLimiter.RequestWindowMs);
        Assert.Equal(3, RecoveryRequestLimiter.RequestLimit);

        // then: имя политики состояния.
        Assert.Equal("recovery_request", RateLimitPolicies.RecoveryRequest);

        // then: ключ — lower(trim(email)).
        Assert.Equal("unknown@example.com", LimiterKeys.FromEmail(" Unknown@Example.com "));
    }
}
