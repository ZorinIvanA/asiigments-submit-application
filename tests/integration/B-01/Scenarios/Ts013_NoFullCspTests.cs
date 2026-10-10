using LabsApp.IntegrationTests.B01.Infrastructure;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-013 «Полная CSP не вводится (только два разрешённых заголовка)»
/// (FR-002, P2).
/// given: wwwroot/index.html существует.
/// when: инспекция заголовков ответа GET /.
/// then: заголовок Content-Security-Policy отсутствует (out_of_scope: «полная
/// CSP»; добавлены только nosniff и DENY — их наличие проверяет кейс TS-012).
/// </summary>
public sealed class Ts013_NoFullCspTests
{
    [Fact]
    public async Task GetRoot_ResponseCarriesNoContentSecurityPolicyHeader()
    {
        // given: wwwroot/index.html существует (TestAssets копируется csproj-целью
        // в content root тестового хоста).
        using var factory = new B01WebAppFactory();
        using var client = HostClients.Create(factory);

        // when: GET / (корень — default-файл статики) и инспекция заголовков.
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // then: заголовок Content-Security-Policy отсутствует.
        Assert.False(
            response.Headers.Contains("Content-Security-Policy"),
            "Ответ несёт Content-Security-Policy, хотя полная CSP — out_of_scope "
            + "(разрешены только X-Content-Type-Options: nosniff и X-Frame-Options: DENY).");
    }
}
