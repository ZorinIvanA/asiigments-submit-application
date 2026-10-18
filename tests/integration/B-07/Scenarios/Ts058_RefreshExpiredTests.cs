using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-058 «Истёкший refresh (TimeProvider): 401» (boundary, FR-014, P0).
///
/// given: вход выполнен; FakeTimeProvider переведён на
///        Auth__RefreshTtlDays+1 суток (expiresAt записи в прошлом).
/// when:  POST /auth/refresh.
/// then:  401. FR-014 AC «Истёкший refresh».
/// </summary>
public sealed class Ts058_RefreshExpiredTests : IClassFixture<B07AuthWebAppFactory>
{
    private readonly B07AuthWebAppFactory _factory;

    public Ts058_RefreshExpiredTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RefreshAfterTtl_Returns401()
    {
        // given: вход выполнен.
        using var client = B07AuthClients.CreateClient(_factory);
        using var login = await B07AuthClients.PostLoginAsync(client, "teacher", B07AuthWebAppFactory.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // given: FakeTimeProvider переведён на Auth__RefreshTtlDays+1 суток.
        _factory.Time.Advance(TimeSpan.FromDays(B07AuthClients.RefreshTtlDays(_factory) + 1));

        // when: POST /auth/refresh.
        using var refresh = await client.PostAsync(B07AuthClients.RefreshEndpoint, content: null);

        // then: 401.
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }
}
