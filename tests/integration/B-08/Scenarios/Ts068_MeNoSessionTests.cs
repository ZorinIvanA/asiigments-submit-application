using LabsApp.IntegrationTests.B08.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-068 «GET /auth/me без сессии: 401» (negative, FR-016, P0).
///
/// given: cookie нет.
/// when:  GET /api/v1/auth/me.
/// then:  401. FR-016 AC «Без сессии».
/// </summary>
public sealed class Ts068_MeNoSessionTests : IClassFixture<B08WebAppFactory>
{
    private readonly B08WebAppFactory _factory;

    public Ts068_MeNoSessionTests(B08WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Me_WithoutSession_Returns401()
    {
        // given: cookie нет (пустой контейнер клиента).
        using var client = B08Host.CreateClient(_factory);
        Assert.Equal(0, client.Cookies.Count);

        // when: GET /api/v1/auth/me.
        using var me = await client.GetAsync(B08Host.MeEndpoint);

        // then: 401.
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }
}
