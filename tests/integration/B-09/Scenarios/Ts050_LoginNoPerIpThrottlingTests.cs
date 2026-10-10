using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-050 «Вход: пер-IP сдерживание отсутствует (заморозка OQ-001)» (scope,
/// FR-004 + OQ-001, P1).
///
/// given: один IP (одно соединение тестового клиента); 10 неизвестных логинов
///        ghost01..ghost10 (Seed__DemoData=false); пер-IP лимит поверх
///        login-лимитера в спеке заморожен (out_of_scope).
/// when:  10 неудачных POST /auth/login — по одному на каждый логин
///        (ротация логинов).
/// then:  все 10 — 401 (ни один не 429): ключ лимитера per-login
///        ('lower(trim(login))|IP' — 10 отдельных ключей по одной метке),
///        общей пер-IP блокировки нет — принятая и задокументированная
///        поверхность (FR-004 реестр (а), OQ-001 default_resolution).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов).
/// </summary>
public sealed class Ts050_LoginNoPerIpThrottlingTests : IClassFixture<B09WebAppFactory>
{
    private const string ClientIp = "10.0.0.50";
    private const string WrongPassword = "whatever1!";

    private readonly B09WebAppFactory _factory;

    public Ts050_LoginNoPerIpThrottlingTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task TenFailedLoginsRotatingUnknownLogins_FromSingleIp_AllUnauthorized_None429()
    {
        // given: один IP (одно соединение тестового клиента); 10 неизвестных
        // логинов ghost01..ghost10 (демо-набор выключен).
        using var client = B09AuthHttp.Create(_factory, ClientIp);

        // when: 10 неудачных POST /auth/login — по одному на каждый логин.
        for (var index = 1; index <= 10; index++)
        {
            var login = $"ghost{index:00}";
            using var response = await B09AuthHttp.LoginAsync(client, login, WrongPassword);

            // then: все 10 — 401 'Неверный логин или пароль' (ни один не 429):
            // пер-IP блокировки нет — ротация логинов не встречает лимита.
            var body = await B09Assertions.ParseObjectAsync(
                response, HttpStatusCode.Unauthorized, $"неудачный вход {login} ({index} из 10)");
            B09Assertions.MessageIs(body, B09AuthSupport.WrongCredentialsMessage);

            // then: ключ лимитера per-login — ровно одна метка в ключе
            // 'ghostNN|IP' (учёт по логину, не по IP).
            Assert.True(
                B09LoginMarkStore.MarksCount(_factory, login, ClientIp) == 1,
                $"Ожидалась ровно одна метка ключа '{login}|{ClientIp}' (per-login учёт).");
        }

        // then: 10 отдельных ключей лимитера (по одному на логин), ни одной
        // общей пер-IP записи — SurfaceRegistry FR-004 (а).
        var limiter = _factory.Services.GetRequiredService<LoginFailureLimiter>();
        Assert.True(
            limiter.TrackedKeysCount == 10,
            $"Ожидалось 10 отдельных per-login ключей лимитера, фактически: {limiter.TrackedKeysCount}.");
    }
}
