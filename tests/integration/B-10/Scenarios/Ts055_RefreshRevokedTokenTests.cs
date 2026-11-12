using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-055 (P0, negative; FR-009, FR-010) «Refresh: отозванный токен — 401».
/// given: Пользователь выполнил logout (токен отозван, revokedAt≠null).
/// when:  POST /auth/refresh с тем же токеном.
/// then:  401 'Не авторизован'. FR-009 AC «Отозванный refresh».
/// </summary>
public sealed class Ts055_RefreshRevokedTokenTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.10.55";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS055_Refresh_AfterLogoutRevokedToken_Returns401()
    {
        // given: пользователь выполнил logout с той же refresh-cookie (токен отозван).
        using var client = B10AuthRequests.Create(_factory, TestIp);
        using var login = await B10AuthRequests.LoginAsync(client, "teacher", B10AuthRequests.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var refreshCookie = B10AuthRequests.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName);
        Assert.False(string.IsNullOrEmpty(refreshCookie), "Вход не выдал refresh-cookie — given неисполним.");
        B10AuthRequests.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshCookie!);

        using var logout = await B10AuthRequests.PostNoBodyAsync(client, B10CookieFlow.LogoutPath);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // when: POST /auth/refresh с тем же токеном.
        using var response = await B10AuthRequests.PostNoBodyAsync(client, B10CookieFlow.RefreshPath);

        // then: 401 'Не авторизован'.
        await ApiAssert.AssertMessageAsync(
            response, HttpStatusCode.Unauthorized, "Не авторизован");
    }
}
