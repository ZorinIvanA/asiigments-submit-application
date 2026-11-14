using LabsApp.Auth;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// Legacy60 (P0, happy_path; FR-010) «Logout: истёкший access + живой refresh —
/// refresh отозван». Кейс СТАРОГО реестра зоны (бывший TS-060), сохранён вне
/// актуального реестра батча (TS-041..TS-050 / TS-056..TS-060) под однозначным
/// ID LegacyNN — logout новым батчем не покрывается (CR-001 раунда 2026-10-10).
/// given: Access-токен истёк — инжектируемые часы хоста переведены на 15 минут
///        (TTL access Auth__AccessTtlMinutes=15, FR-003/ADR-002), refresh валиден
///        (TTL 7 суток); сессия получена входом DI-сид-студента.
/// when:  POST /api/v1/auth/logout с refresh-cookie и истёкшим access-cookie
///        (без валидного access).
/// then:  204 и refresh отозван, несмотря на невалидный access — последующий
///        POST /auth/refresh с тем же значением — 401 «Не авторизован» (FR-010
///        AC «Истёкший access + живой refresh»).
/// </summary>
public sealed class Legacy60_LogoutExpiredAccessRevokesRefreshTests(B11TimedWebAppFactory factory)
    : IClassFixture<B11TimedWebAppFactory>
{
    private readonly B11TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task Legacy60_Logout_WithExpiredAccessAndLiveRefresh_RevokesRefresh()
    {
        // given: валидная сессия студента (вход фиксирует значения обеих cookie).
        B11RecoverySeed.AddStudent(
            _factory,
            "b11ts060.student",
            "b11ts060.student@example.com",
            "Parol1234!");
        using var client = HostClients.Create(_factory);
        using var login = await HostClients.LoginAsync(client, "b11ts060.student", "Parol1234!");
        _ = await ApiAssert.ReadOkJsonAsync(login);

        var refreshValue = HostClients.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName)
            ?? throw new InvalidOperationException(
                "Вход не вернул Set-Cookie refresh_token — given кейса неисполним.");
        var accessValue = HostClients.SetCookieValue(login, AuthCoreDefaults.AccessTokenCookieName)
            ?? throw new InvalidOperationException(
                "Вход не вернул Set-Cookie access_token — given кейса неисполним.");

        // given: часы переведены за 15 минут — access истёк (exp = iat + 15 мин,
        // живость «строго»), refresh ещё жив (TTL 7 суток).
        _factory.Time.Advance(TimeSpan.FromMinutes(15));

        // when: POST /auth/logout с refresh-cookie и истёкшим access-cookie.
        using var logoutClient = HostClients.Create(_factory);
        logoutClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={accessValue}; " +
            $"{AuthCoreDefaults.RefreshTokenCookieName}={refreshValue}");
        using var logout = await HostClients.PostWithoutBodyAsync(logoutClient, HostClients.LogoutPath);

        // then: 204 — валидный access не требуется.
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // then: refresh отозван, несмотря на невалидный access — последующий
        // /auth/refresh с тем же значением — 401 «Не авторизован».
        using var refreshClient = HostClients.Create(_factory);
        HostClients.SetRequestCookie(refreshClient, AuthCoreDefaults.RefreshTokenCookieName, refreshValue);
        using var refresh = await HostClients.PostWithoutBodyAsync(refreshClient, HostClients.RefreshPath);
        await ApiAssert.AssertMessageAsync(
            refresh,
            HttpStatusCode.Unauthorized,
            "Не авторизован");
    }
}
