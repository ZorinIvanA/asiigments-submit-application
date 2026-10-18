using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-054 (P1, negative; FR-009) «Refresh: просроченный токен — 401 без
/// Set-Cookie».
/// given: Refresh-токен с expiresAt в прошлом (инжектируемые часы переведены за
///        7 дней после выпуска — TTL refresh Auth__RefreshTtlDays=7 суток).
/// when:  POST /auth/refresh с этим токеном.
/// then:  401 'Не авторизован'; заголовки Set-Cookie отсутствуют. FR-009 AC
///        «Просроченный refresh».
/// </summary>
public sealed class Ts054_RefreshExpiredTokenTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.10.54";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS054_Refresh_WithExpiredRefreshToken_Returns401WithoutSetCookie()
    {
        // given: refresh-cookie выпущена при входе (expiresAt = выпуск + 7 суток).
        using var client = B10AuthRequests.Create(_factory, TestIp);
        using var login = await B10AuthRequests.LoginAsync(client, "teacher", B10AuthRequests.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var refreshCookie = B10AuthRequests.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName);
        Assert.False(string.IsNullOrEmpty(refreshCookie), "Вход не выдал refresh-cookie — given неисполним.");
        B10AuthRequests.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshCookie!);

        // when: часы переведены за 7 дней после выпуска — expiresAt в прошлом
        // (контракт: просрочен при expiresAt ≤ now).
        _factory.Time.Advance(TimeSpan.FromDays(7));
        using var response = await B10AuthRequests.PostNoBodyAsync(client, B10CookieFlow.RefreshPath);

        // then: 401 'Не авторизован'; заголовки Set-Cookie отсутствуют.
        await ApiAssert.AssertMessageAsync(
            response, HttpStatusCode.Unauthorized, "Не авторизован");
        Assert.Empty(B10AuthRequests.SetCookies(response));
    }
}
