using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-058 (P0, happy_path; FR-010, NFR-007) «Logout: валидная сессия — 204,
/// refresh отозван, cookie сброшены».
/// given: Пользователь вошёл (оба cookie установлены).
/// when:  POST /auth/logout.
/// then:  204; refresh-токен отозван (последующий /auth/refresh — 401); оба cookie
///        сброшены (Set-Cookie с Max-Age=0). FR-010 AC «Выход с валидной сессией».
/// </summary>
public sealed class Ts058_LogoutActiveSessionTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.58";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS058_Logout_WithActiveSession_Returns204ClearsBothCookiesAndRevokesRefresh()
    {
        // given: пользователь вошёл (оба cookie установлены; значения зафиксированы).
        using var client = B09AuthHttp.Create(_factory, TestIp);
        using var login = await B09AuthHttp.LoginAsync(client, "teacher", B09AuthHttp.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var accessToken = B09AuthHttp.SetCookieValue(login, AuthCoreDefaults.AccessTokenCookieName);
        var refreshCookie = B09AuthHttp.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName);
        Assert.False(string.IsNullOrEmpty(accessToken), "Вход не выдал access-cookie — given неисполним.");
        Assert.False(string.IsNullOrEmpty(refreshCookie), "Вход не выдал refresh-cookie — given неисполним.");
        B09AuthHttp.SetRequestCookies(
            client,
            (AuthCoreDefaults.AccessTokenCookieName, accessToken!),
            (AuthCoreDefaults.RefreshTokenCookieName, refreshCookie!));

        // when: POST /auth/logout.
        using var response = await B09AuthHttp.PostNoBodyAsync(client, B09AuthHttp.LogoutPath);

        // then: 204; оба cookie сброшены — Set-Cookie с Max-Age=0.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(
            B09AuthHttp.TryGetSetCookieLine(response, AuthCoreDefaults.AccessTokenCookieName, out var accessClear),
            "Logout не сбросил cookie access_token.");
        Assert.Contains("max-age=0", accessClear!, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            B09AuthHttp.TryGetSetCookieLine(response, AuthCoreDefaults.RefreshTokenCookieName, out var refreshClear),
            "Logout не сбросил cookie refresh_token.");
        Assert.Contains("max-age=0", refreshClear!, StringComparison.OrdinalIgnoreCase);

        // then: refresh-токен отозван — последующий /auth/refresh с прежним значением — 401.
        using var after = await B09AuthHttp.PostNoBodyAsync(client, B09AuthHttp.RefreshPath);
        var body = await B09Assertions.ParseObjectAsync(after, HttpStatusCode.Unauthorized, "TS-058: refresh после logout");
        B09Assertions.MessageIs(body, "Не авторизован");
    }
}
