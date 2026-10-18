using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-059 (P0, negative; FR-009) «Refresh: без cookie — 401».
/// given: Запрос не содержит cookie refresh_token.
/// when:  POST /auth/refresh.
/// then:  401 'Не авторизован' (FR-009 AC «Без cookie»).
/// </summary>
public sealed class Ts059_RefreshWithoutCookieReturns401Tests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS059_Refresh_WithoutRefreshCookie_Returns401()
    {
        // given: запрос не содержит cookie refresh_token (клиент без cookie).
        using var client = HostClients.Create(_factory);

        // when: POST /auth/refresh.
        using var response = await HostClients.PostWithoutBodyAsync(client, HostClients.RefreshPath);

        // then: 401 'Не авторизован'.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Unauthorized,
            "Не авторизован",
            exactSingleMessageProperty: true);
    }
}
