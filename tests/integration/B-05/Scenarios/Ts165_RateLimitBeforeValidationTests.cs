using System.Text;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-165 (P0, negative; FR-080) «429 раньше 400: ключ входа усечён до
/// 100 символов».
///
/// given: 5 неуспешных входов по ключу (login='A'×100, IP=10.0.0.1) —
///        неизвестный логин, каждый по 401.
/// when:  6-й вход с телом {login:'A'×1000, password:1} (нестроковый password —
///        тело семантически невалидно).
/// then:  429 (не 400): ключ нормализован усечением до 100 симв., счёт вёлся
///        по нему же. FR-080 AC «429 раньше 400».
/// </summary>
public sealed class Ts165_RateLimitBeforeValidationTests(B05SecurityWebAppFactory factory)
    : IClassFixture<B05SecurityWebAppFactory>
{
    private const string ClientIp = "10.0.0.1";

    private readonly B05SecurityWebAppFactory _factory = factory;

    [Fact]
    public async Task TS165_SixthLoginWithTruncatedKey_Returns429BeforeBodyValidation()
    {
        // given: 5 неуспешных входов по ключу (login='A'×100, IP=10.0.0.1).
        using var client = B05SecurityClients.CreateClientWithIp(_factory, ClientIp);
        var hundredAs = new string('A', 100);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var failed = await B05SecurityClients.PostLoginAsync(
                client, hundredAs, B05SecurityClients.CasePassword);
            Assert.True(
                failed.StatusCode == HttpStatusCode.Unauthorized,
                $"Предусловие кейса: неуспешный вход {attempt} должен дать 401, " +
                $"фактически {(int)failed.StatusCode}: {await failed.Content.ReadAsStringAsync()}");
        }

        // when: 6-й вход с телом {login:'A'×1000, password:1} — ключ при
        //       нормализации усекается до 'a'×100, т.е. совпадает с ключом счёта.
        var thousandAs = new string('A', 1000);
        using var sixth = await PostRawLoginAsync(
            client,
            """{"login":""" + "\"" + thousandAs + "\"" + ""","password":1}""");

        // then: 429 (не 400): лимитер проверяется до семантической валидации тела,
        //       счёт вёлся по усечённому ключу.
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, sixth.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(sixth);
        BodyAssertions.MessageIs(root, B05SecurityClients.RateLimitedMessage);
    }

    /// <summary>POST /auth/login с дословным JSON-телом (нестроковый password:1).</summary>
    private static Task<HttpResponseMessage> PostRawLoginAsync(HttpClient client, string json)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return client.PostAsync(B05SecurityClients.LoginEndpoint, content);
    }
}
