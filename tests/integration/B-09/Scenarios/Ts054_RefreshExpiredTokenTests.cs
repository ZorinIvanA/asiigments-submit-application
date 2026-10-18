using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-054 (P1, negative; FR-009) «Refresh: просроченный токен — 401 без
/// Set-Cookie».
/// given: Refresh-токен с expiresAt в прошлом (инжектируемые часы переведены за
///        7 дней после выпуска — TTL refresh Auth__RefreshTtlDays=7 суток).
/// when:  POST /auth/refresh с этим токеном.
/// then:  401 'Не авторизован'; заголовки Set-Cookie отсутствуют. FR-009 AC
///        «Просроченный refresh».
/// </summary>
public sealed class Ts054_RefreshExpiredTokenTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.54";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS054_Refresh_WithExpiredRefreshToken_Returns401WithoutSetCookie()
    {
        // given: refresh-cookie выпущена при входе (expiresAt = выпуск + 7 суток).
        using var client = B09AuthHttp.Create(_factory, TestIp);
        using var login = await B09AuthHttp.LoginAsync(client, "teacher", B09AuthHttp.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var refreshCookie = B09AuthHttp.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName);
        Assert.False(string.IsNullOrEmpty(refreshCookie), "Вход не выдал refresh-cookie — given неисполним.");
        B09AuthHttp.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshCookie!);

        // when: часы переведены за 7 дней после выпуска — expiresAt в прошлом
        // (контракт: просрочен при expiresAt ≤ now).
        _factory.Time.Advance(TimeSpan.FromDays(7));
        using var response = await B09AuthHttp.PostNoBodyAsync(client, B09AuthHttp.RefreshPath);

        // then: 401 'Не авторизован'; заголовки Set-Cookie отсутствуют.
        var body = await B09Assertions.ParseObjectAsync(response, HttpStatusCode.Unauthorized, "TS-054: просроченный refresh");
        B09Assertions.MessageIs(body, "Не авторизован");
        Assert.Empty(B09AuthHttp.SetCookies(response));
    }
}
