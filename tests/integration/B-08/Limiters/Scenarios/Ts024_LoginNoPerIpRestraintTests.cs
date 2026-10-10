using LabsApp.IntegrationTests.B08.Limiters.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// TS-024 «Заморозка пер-IP сдерживания входа: ротация логинов не даёт 429»
/// (scope, FR-004 + FR-007, P1).
///
/// given: хост Development; один IP клиента; лимитеры пусты (свежая фикстура);
///        20 несуществующих логинов ghost01..ghost20.
/// when:  20 POST /auth/login — по одной неудачной попытке на каждый логин.
/// then:  все 20 — 401 'Неверный логин или пароль'; ни один — не 429 (пер-IP
///        окно поверх login-лимитера отсутствует — out_of_scope/OQ-001; каждый
///        ключ 'login|IP' имеет собственное окно 5/60с).
/// </summary>
public sealed class Ts024_LoginNoPerIpRestraintTests : IClassFixture<B08LimitersRateLimitWebAppFactory>
{
    private const string ClientIp = "10.19.7.3";
    private const string WrongPassword = "definitely-wrong-pass";

    private readonly B08LimitersRateLimitWebAppFactory _factory;

    public Ts024_LoginNoPerIpRestraintTests(B08LimitersRateLimitWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_RotatedUnknownLoginsFromOneIp_All401_None429()
    {
        // given: один IP клиента; лимитеры пусты (свежая фикстура).
        using var client = B08LimitersClients.CreateClientWithIp(_factory, ClientIp);

        // when: по одной неудачной попытке на каждый из логинов ghost01..ghost20.
        for (var i = 1; i <= 20; i++)
        {
            var login = $"ghost{i:D2}";
            using var response = await B08LimitersClients.PostLoginAsync(client, login, WrongPassword);

            // then: 401 'Неверный логин или пароль' — ни один не 429.
            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized,
                $"Попытка {login}: ожидался 401, фактически {(int)response.StatusCode} (429 недопустим — пер-IP сдерживания нет).");
            var envelope = await B08LimitersBodyAssertions.ReadRootObjectAsync(response);
            B08LimitersBodyAssertions.MessageIs(envelope, "Неверный логин или пароль");
        }
    }
}
