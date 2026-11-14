using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-055 (P0, negative; FR-009, FR-010) «Refresh: отозванный токен — 401».
/// given: Пользователь выполнил logout (токен отозван, revokedAt≠null).
/// when:  POST /auth/refresh с тем же токеном.
/// then:  401 'Не авторизован'. FR-009 AC «Отозванный refresh».
/// </summary>
public sealed class Ts055_RefreshRevokedTokenTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.55";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS055_Refresh_AfterLogoutRevokedToken_Returns401()
    {
        // given: пользователь выполнил logout с той же refresh-cookie (токен отозван).
        using var client = B09AuthHttp.Create(_factory, TestIp);
        using var login = await B09AuthHttp.LoginAsync(client, "teacher", B09AuthHttp.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var refreshCookie = B09AuthHttp.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName);
        Assert.False(string.IsNullOrEmpty(refreshCookie), "Вход не выдал refresh-cookie — given неисполним.");
        B09AuthHttp.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshCookie!);

        using var logout = await B09AuthHttp.PostNoBodyAsync(client, B09AuthHttp.LogoutPath);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // when: POST /auth/refresh с тем же токеном.
        using var response = await B09AuthHttp.PostNoBodyAsync(client, B09AuthHttp.RefreshPath);

        // then: 401 'Не авторизован'.
        var body = await B09Assertions.ParseObjectAsync(response, HttpStatusCode.Unauthorized, "TS-055: отозванный refresh");
        B09Assertions.MessageIs(body, "Не авторизован");
    }
}
