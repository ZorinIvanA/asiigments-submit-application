using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-026 «Защищённый эндпоинт без токена: 401 "Не авторизован"» (негативный, P0,
/// FR-011 AC «Без токена»).
///
/// given: cookie отсутствуют.
/// when:  GET /api/v1/labs.
/// then:  HTTP 401; тело {"message":"Не авторизован"}.
/// </summary>
public sealed class Ts026_UnauthorizedWithoutTokenTests : IClassFixture<B06WebAppFactory>
{
    private readonly B06WebAppFactory _factory;

    public Ts026_UnauthorizedWithoutTokenTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetLabsWithoutCookies_Returns401WithUnauthorizedEnvelope()
    {
        // given: cookie отсутствуют — новый клиент с пустым CookieContainer.
        using var client = HostClients.Create(_factory);

        // when: GET /api/v1/labs.
        using var response = await client.GetAsync(ApiRequests.LabsEndpoint);

        // then: HTTP 401 с телом {"message":"Не авторизован"} — ровно один ключ.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "message");
        BodyAssertions.MessageIs(root, "Не авторизован");
    }
}
