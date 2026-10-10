using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-058 (P0, happy_path; FR-010, NFR-007) «Logout: валидная сессия — 204,
/// refresh отозван, cookie сброшены».
/// given: Пользователь вошёл (оба cookie установлены).
/// when:  POST /auth/logout.
/// then:  204; refresh-токен отозван (последующий /auth/refresh — 401); оба cookie
///        сброшены (Set-Cookie с Max-Age=0). FR-010 AC «Выход с валидной сессией».
/// </summary>
public sealed class Ts058_LogoutActiveSessionTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.10.58";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS058_Logout_WithActiveSession_Returns204ClearsBothCookiesAndRevokesRefresh()
    {
        // given: пользователь вошёл (оба cookie установлены; значения зафиксированы).
        using var client = B10AuthRequests.Create(_factory, TestIp);
        using var login = await B10AuthRequests.LoginAsync(client, "teacher", B10AuthRequests.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var accessToken = B10AuthRequests.SetCookieValue(login, AuthCoreDefaults.AccessTokenCookieName);
        var refreshCookie = B10AuthRequests.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName);
        Assert.False(string.IsNullOrEmpty(accessToken), "Вход не выдал access-cookie — given неисполним.");
        Assert.False(string.IsNullOrEmpty(refreshCookie), "Вход не выдал refresh-cookie — given неисполним.");
        B10AuthRequests.SetRequestCookies(
            client,
            (AuthCoreDefaults.AccessTokenCookieName, accessToken!),
            (AuthCoreDefaults.RefreshTokenCookieName, refreshCookie!));

        // when: POST /auth/logout.
        using var response = await B10AuthRequests.PostNoBodyAsync(client, B10CookieFlow.LogoutPath);

        // then: 204; оба cookie сброшены — Set-Cookie с Max-Age=0.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cleared = B10AuthRequests.SetCookies(response);
        var accessClear = cleared.SingleOrDefault(cookie => cookie.Name == AuthCoreDefaults.AccessTokenCookieName);
        Assert.True(accessClear is not null, "Logout не сбросил cookie access_token.");
        Assert.Equal("0", accessClear!.Attribute("Max-Age"));
        var refreshClear = cleared.SingleOrDefault(cookie => cookie.Name == AuthCoreDefaults.RefreshTokenCookieName);
        Assert.True(refreshClear is not null, "Logout не сбросил cookie refresh_token.");
        Assert.Equal("0", refreshClear!.Attribute("Max-Age"));

        // then: refresh-токен отозван — последующий /auth/refresh с прежним значением — 401.
        using var after = await B10AuthRequests.PostNoBodyAsync(client, B10CookieFlow.RefreshPath);
        await ApiAssert.AssertMessageAsync(
            after, HttpStatusCode.Unauthorized, "Не авторизован");
    }
}
