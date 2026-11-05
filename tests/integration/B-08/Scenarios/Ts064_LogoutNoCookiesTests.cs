using LabsApp.Auth;
using LabsApp.IntegrationTests.B08.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-064 «Logout без обеих cookie» (negative, FR-015, P0).
///
/// given: обе cookie отсутствуют.
/// when:  POST /api/v1/auth/logout.
/// then:  204 — logout ИДЕМПОТЕНТЕН и отвечает 204 всегда (ADR-010/ASM-016:
///        «logout → всегда 204»; отзыва нет — запись Api.Security не создаётся,
///        но ответ остаётся 204 без конверта ошибки).
/// </summary>
public sealed class Ts064_LogoutNoCookiesTests : IClassFixture<B08WebAppFactory>
{
    private readonly B08WebAppFactory _factory;

    public Ts064_LogoutNoCookiesTests(B08WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Logout_WithoutAnyCookies_ReturnsIdempotent204()
    {
        // given: обе cookie отсутствуют (пустой контейнер клиента).
        using var client = B08Host.CreateClient(_factory);
        Assert.Equal(0, client.Cookies.Count);

        // when: POST /api/v1/auth/logout.
        using var logout = await client.PostAsync(B08Host.LogoutEndpoint, json: null);

        // then: 204 (идемпотентный logout — ADR-010/ASM-016, без конверта).
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }
}
