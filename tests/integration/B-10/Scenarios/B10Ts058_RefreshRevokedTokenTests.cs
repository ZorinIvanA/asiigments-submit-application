using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-058 (P0, negative; FR-009, FR-010) «Refresh: отозванный токен — 401»
/// (актуальная нумерация кейсов батча B-10; родственный тест предыдущей нумерации
/// — Ts055_RefreshRevokedTokenTests).
/// given: Пользователь выполнил logout — его refresh-токен отозван
///        (revokedAt≠null).
/// when:  POST /auth/refresh с тем же токеном.
/// then:  401 'Не авторизован' (AC FR-009 «Отозванный refresh»).
/// </summary>
public sealed class B10Ts058_RefreshRevokedTokenTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.20.58";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS058_Refresh_AfterLogoutRevocation_Returns401()
    {
        // given: пользователь выполнил logout с той же refresh-cookie (токен
        // отозван, revokedAt≠null).
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
