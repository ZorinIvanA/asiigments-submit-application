using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-059 «Refresh без cookie: 401» (negative, FR-014, P0).
///
/// given: cookie refresh_token отсутствует.
/// when:  POST /auth/refresh.
/// then:  401. FR-014 AC «Без cookie».
/// </summary>
public sealed class Ts059_RefreshNoCookieTests : IClassFixture<B07AuthWebAppFactory>
{
    private readonly B07AuthWebAppFactory _factory;

    public Ts059_RefreshNoCookieTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RefreshWithoutCookie_Returns401()
    {
        // given: cookie refresh_token отсутствует (свежий клиент без входа).
        using var client = B07AuthClients.CreateClient(_factory);

        // when: POST /auth/refresh.
        using var refresh = await client.PostAsync(B07AuthClients.RefreshEndpoint, content: null);

        // then: 401.
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }
}
