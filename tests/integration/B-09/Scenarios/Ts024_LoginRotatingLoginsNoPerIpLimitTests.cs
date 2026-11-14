using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-024 «Заморозка пер-IP сдерживания входа не нарушена (OQ-001)»
/// (scope, FR-004 + FR-007, P1).
///
/// given: один IP тестового хоста; 6 несуществующих логинов ghost01..ghost06
///        (разные ключи лимитера); меток нет (демо-набор выключен).
/// when:  6 неудачных POST /auth/login с разными неизвестными логинами в
///        пределах 60 с.
/// then:  все 6 — 401 'Неверный логин или пароль' (ни один не 429): нового
///        пер-IP лимита поверх матрицы FR-004 не введено (out_of_scope
///        «НОВЫЕ лимитеры... ЗАМОРОЖЕНО»; OQ-001).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файл прежней волны
/// зоны с совпадающим поведением (Ts050_LoginNoPerIpThrottling) не изменялся.
/// </summary>
public sealed class Ts024_LoginRotatingLoginsNoPerIpLimitTests : IClassFixture<B09WebAppFactory>
{
    private const string ClientIp = "10.0.0.74";
    private const string WrongPassword = "whatever1!";

    private readonly B09WebAppFactory _factory;

    public Ts024_LoginRotatingLoginsNoPerIpLimitTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SixFailedLoginsRotatingUnknownLogins_FromOneIp_All401_None429()
    {
        // given: один IP тестового хоста; меток нет (свежий хост фикстуры).
        using var client = B09AuthHttp.Create(_factory, ClientIp);

        // when: 6 неудачных POST /auth/login с разными неизвестными логинами
        // ghost01..ghost06 в пределах 60 с.
        for (var index = 1; index <= 6; index++)
        {
            var login = $"ghost{index:00}";
            using var response = await B09AuthHttp.LoginAsync(client, login, WrongPassword);

            // then: все 6 — 401 'Неверный логин или пароль' (ни один не 429):
            // пер-IP лимита нет — каждый логин образует свой ключ лимитера.
            Assert.True(
                response.StatusCode != HttpStatusCode.TooManyRequests,
                $"Вход '{login}' не должен упираться в пер-IP лимит (429), фактически " +
                $"{(int)response.StatusCode}.");
            var body = await B09Assertions.ParseObjectAsync(
                response, HttpStatusCode.Unauthorized, $"неудачный вход '{login}' ({index} из 6)");
            B09Assertions.MessageIs(body, B09AuthSupport.WrongCredentialsMessage);
        }
    }
}
