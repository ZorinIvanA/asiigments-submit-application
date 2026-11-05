using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-053 (P0, happy_path; FR-009) «Refresh: успех — 204, новый access, refresh
/// не переустанавливается».
/// given: Валидная refresh-cookie активной сессии.
/// when:  POST /auth/refresh; затем повторный POST /auth/refresh с той же cookie.
/// then:  Оба — 204; каждый ответ содержит Set-Cookie нового access_token;
///        refresh_token не переустанавливается и не ротируется (исходный действует
///        до истечения/отзыва). FR-009 AC «Успешный refresh».
/// </summary>
public sealed class Ts053_RefreshSuccessTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.53";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS053_Refresh_WithLiveRefreshCookie_Returns204NewAccessWithoutRefreshRotation()
    {
        // given: валидная refresh-cookie активной сессии (вход сид-преподавателя;
        // значения обеих cookie зафиксированы; часы сдвигаются явно —(access-)токены
        // последовательных выпусков различимы по iat).
        using var client = B09AuthHttp.Create(_factory, TestIp);
        using var login = await B09AuthHttp.LoginAsync(client, "teacher", B09AuthHttp.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var accessAtLogin = B09AuthHttp.SetCookieValue(login, AuthCoreDefaults.AccessTokenCookieName);
        var refreshCookie = B09AuthHttp.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName);
        Assert.False(string.IsNullOrEmpty(refreshCookie), "Вход не выдал refresh-cookie — given неисполним.");
        B09AuthHttp.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshCookie!);

        // when (1): POST /auth/refresh.
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        using var first = await B09AuthHttp.PostNoBodyAsync(client, B09AuthHttp.RefreshPath);

        // then (1): 204; Set-Cookie НОВОГО access_token; refresh_token не переустановлен.
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        var firstAccess = B09AuthHttp.SetCookieValue(first, AuthCoreDefaults.AccessTokenCookieName);
        Assert.True(firstAccess is not null, "Refresh не выпустил Set-Cookie access_token.");
        Assert.NotEqual(accessAtLogin, firstAccess);
        Assert.False(
            B09AuthHttp.HasSetCookie(first, AuthCoreDefaults.RefreshTokenCookieName),
            "Refresh не должен переустанавливать refresh_token.");

        // when (2): повторный POST /auth/refresh с ТОЙ ЖЕ cookie.
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        using var second = await B09AuthHttp.PostNoBodyAsync(client, B09AuthHttp.RefreshPath);

        // then (2): снова 204; снова новый access; refresh не ротирован — исходная
        // cookie действительна (токен действует до истечения/отзыва).
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var secondAccess = B09AuthHttp.SetCookieValue(second, AuthCoreDefaults.AccessTokenCookieName);
        Assert.True(secondAccess is not null, "Повторный refresh не выпустил Set-Cookie access_token.");
        Assert.NotEqual(firstAccess, secondAccess);
        Assert.False(
            B09AuthHttp.HasSetCookie(second, AuthCoreDefaults.RefreshTokenCookieName),
            "Повторный refresh не должен переустанавливать/ротировать refresh_token.");
    }
}
