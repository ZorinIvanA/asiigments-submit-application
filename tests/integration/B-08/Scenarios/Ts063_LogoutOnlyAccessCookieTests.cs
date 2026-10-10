using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-063 «Logout только с access cookie: 204» (happy_path, FR-015, P1).
///
/// given: refresh отсутствует; access действителен (минт сессии, затем refresh-
///        cookie убирается из контейнера — серверу отправляется только access).
/// when:  POST /api/v1/auth/logout с access cookie.
/// then:  204; обе cookie очищены (Set-Cookie Max-Age=0).
///        FR-015 AC «Только access cookie».
/// </summary>
public sealed class Ts063_LogoutOnlyAccessCookieTests : IClassFixture<B08WebAppFactory>
{
    private const string Login = "ts063-student";
    private const string Email = "ts063@lab.local";
    private const string FullName = "Студент ШестьдесятТри";

    private readonly B08WebAppFactory _factory;

    public Ts063_LogoutOnlyAccessCookieTests(B08WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Logout_WithOnlyAccessCookie_Returns204AndClearsBothCookies()
    {
        // given: access действителен, refresh отсутствует.
        using var client = B08Host.CreateClient(_factory);
        var user = B08Host.SeedStudent(_factory, Login, Email, FullName);
        B08Host.EstablishSession(_factory, client, user.Id, UserRoles.Student);
        Assert.True(
            client.Cookies.Remove(AuthCoreDefaults.RefreshTokenCookieName),
            "Предусловие: refresh-cookie удалена из контейнера.");
        Assert.True(client.Cookies.Contains(AuthCoreDefaults.AccessTokenCookieName), "Предусловие: access-cookie установлена.");

        // when: POST /api/v1/auth/logout только с access cookie.
        using var logout = await client.PostAsync(B08Host.LogoutEndpoint, json: null);

        // then: 204; обе cookie очищены.
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        ResponseAssertions.AssertCookieClearedByMaxAgeZero(logout, AuthCoreDefaults.AccessTokenCookieName);
        ResponseAssertions.AssertCookieClearedByMaxAgeZero(logout, AuthCoreDefaults.RefreshTokenCookieName);
    }
}
