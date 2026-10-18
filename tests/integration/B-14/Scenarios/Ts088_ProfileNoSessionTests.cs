using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-088 «GET /me/profile без сессии: 401» (negative, FR-020, P0).
///
/// given: Cookie нет — клиент без cookie-контейнера и без заголовка Cookie.
/// when:  GET /api/v1/me/profile.
/// then:  401. FR-020 AC «Без сессии».
/// </summary>
public sealed class Ts088_ProfileNoSessionTests : IClassFixture<B14WebAppFactory>
{
    private readonly B14WebAppFactory _factory;

    public Ts088_ProfileNoSessionTests(B14WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileWithoutCookie_ReturnsUnauthorized()
    {
        // given: клиент без cookie (HandleCookies=false, заголовок Cookie не задаётся).
        using var client = B14Harness.Create(_factory);

        // when: GET /api/v1/me/profile.
        using var response = await client.GetAsync(B14Harness.ProfileEndpoint);

        // then: 401.
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
