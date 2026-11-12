using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-152 «NFR-007: поэкземплярная матрица Set-Cookie (7 инстансов, 4
/// эндпойнта)» (nfr, NFR-007 + FR-008 + FR-009 + FR-010, P0).
///
/// given: Dev-хост с возможностью выполнить register, login, refresh, logout;
///        отдельно — Production-конфигурация (unit-уровень параметров cookie:
///        Auth__JwtKey и нестандартный Seed__TeacherPassword заданы).
/// when:  разбор всех Set-Cookie: register (2), login (2), refresh (1),
///        logout (2).
/// then:  всего 7 инстансов; HttpOnly, SameSite=Strict, Path=/ — 7 из 7;
///        Max-Age: 900 — у всех 3 выпусков access (register/login/refresh),
///        604800 — у обоих выпусков refresh (register/login), 0 — у обоих
///        сбросов logout; refresh-эндпойнт НЕ переустанавливает refresh_token;
///        Secure присутствует 7/7 в Production и 0/7 в Development
///        (NFR-007/AR-001).
///
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-08/Auth (староволновые Ts191/Ts192 в B-10 и Ts029/Ts030
/// в B-06 помечены арбитражем дубликатами). Поведенческая часть кейса исполнима
/// дословно и исполнена в собственной зоне батча B-09 (прецедент c-1052);
/// расхождение размещения зафиксировано в scenario_change_requests.
/// </summary>
public sealed class Ts152_Nfr007SetCookieMatrixTests
{
    private const string LoginName = "b09matrix";
    private const string Email = "b09matrix@example.com";
    private const string FullName = "Матрица Куки Девять";
    private const string Password = "Passw0rd!";

    [Fact]
    public async Task DevelopmentHost_SevenSetCookieInstances_FullMatrixWithoutSecure()
    {
        var (register, login, refresh, logout) = await RunFourEndpointFlow(new B09AuthDevFactory());

        AssertInstanceMatrix(register, login, refresh, logout, expectSecure: false);
    }

    [Fact]
    public async Task ProductionHost_SevenSetCookieInstances_SecureOnAllSeven()
    {
        var (register, login, refresh, logout) = await RunFourEndpointFlow(new B09AuthProductionFactory());

        AssertInstanceMatrix(register, login, refresh, logout, expectSecure: true);
    }

    /// <summary>Матрица NFR-007 по экземплярам: состав, атрибуты, Max-Age, Secure.</summary>
    private static void AssertInstanceMatrix(
        IReadOnlyList<B09AuthSetCookie> register,
        IReadOnlyList<B09AuthSetCookie> login,
        IReadOnlyList<B09AuthSetCookie> refresh,
        IReadOnlyList<B09AuthSetCookie> logout,
        bool expectSecure)
    {
        // then: всего 7 инстансов: register (2), login (2), refresh (1), logout (2).
        Assert.Equal(2, register.Count);
        Assert.Equal(2, login.Count);
        Assert.Single(refresh);
        Assert.Equal(2, logout.Count);

        var environment = expectSecure ? "Production" : "Development";

        // then: состав пар «имя → Max-Age» по эндпойнтам; refresh-эндпойнт НЕ
        // переустанавливает refresh_token.
        Assert.Equal(
            new (string, string?)[] { ("access_token", "900"), ("refresh_token", "604800") },
            register.Select(cookie => (cookie.Name, cookie.Attribute("max-age"))).ToArray());
        Assert.Equal(
            new (string, string?)[] { ("access_token", "900"), ("refresh_token", "604800") },
            login.Select(cookie => (cookie.Name, cookie.Attribute("max-age"))).ToArray());
        Assert.Equal(
            new (string, string?)[] { ("access_token", "900") },
            refresh.Select(cookie => (cookie.Name, cookie.Attribute("max-age"))).ToArray());
        Assert.Equal(
            new (string, string?)[] { ("access_token", "0"), ("refresh_token", "0") },
            logout.Select(cookie => (cookie.Name, cookie.Attribute("max-age"))).ToArray());

        // then: HttpOnly, SameSite=Strict, Path=/ — 7 из 7.
        var all = register.Concat(login).Concat(refresh).Concat(logout).ToList();
        Assert.Equal(7, all.Count);
        foreach (var cookie in all)
        {
            Assert.True(cookie.HasFlag("httponly"), $"[{environment}] {cookie.Name}: нет HttpOnly.");
            Assert.Equal("strict", cookie.Attribute("samesite"));
            Assert.Equal("/", cookie.Attribute("path"));
        }

        // then: Secure — 7/7 в Production и 0/7 в Development.
        var secureCount = all.Count(cookie => cookie.HasFlag("secure"));
        Assert.Equal(expectSecure ? 7 : 0, secureCount);
    }

    /// <summary>
    /// given/when: последовательность register → login → refresh → logout на
    /// одном клиенте (cookie переносятся вручную — B09AuthHttp); возвращает
    /// экземпляры Set-Cookie каждого ответа.
    /// </summary>
    private static async Task<(
        IReadOnlyList<B09AuthSetCookie> Register,
        IReadOnlyList<B09AuthSetCookie> Login,
        IReadOnlyList<B09AuthSetCookie> Refresh,
        IReadOnlyList<B09AuthSetCookie> Logout)> RunFourEndpointFlow(B09AuthWebAppFactory factory)
    {
        using var client = B09AuthSupport.CreateClient(factory);

        // register → 201, ровно 2 Set-Cookie.
        IReadOnlyList<B09AuthSetCookie> registerCookies;
        using (var register = await B09AuthHttp.PostJsonAsync(client, B09AuthSupport.RegisterEndpoint,
                   "{\"fullName\":\"" + FullName + "\",\"login\":\"" + LoginName + "\",\"email\":\"" + Email + "\"," +
                   "\"password\":\"" + Password + "\",\"repeatPassword\":\"" + Password + "\"}"))
        {
            B09AuthSupport.AssertStatus(register, HttpStatusCode.Created, "register (TS-152)");
            registerCookies = B09AuthSupport.ParseSetCookie(register);
        }

        // login → 200, ровно 2 Set-Cookie (cookie регистрации перенесены — вход
        // по телу, cookie ответа обновляются).
        IReadOnlyList<B09AuthSetCookie> loginCookies;
        using (var login = await B09AuthHttp.PostJsonAsync(client, B09AuthSupport.LoginEndpoint,
                   "{\"login\":\"" + LoginName + "\",\"password\":\"" + Password + "\"}"))
        {
            B09AuthSupport.AssertStatus(login, HttpStatusCode.OK, "login (TS-152)");
            loginCookies = B09AuthSupport.ParseSetCookie(login);
            B09AuthHttp.SetRequestCookies(
                client, (loginCookies[0].Name, loginCookies[0].Value), (loginCookies[1].Name, loginCookies[1].Value));
        }

        // refresh → 204, ровно 1 Set-Cookie (по refresh-cookie из заголовка;
        // refresh_token НЕ переустанавливается).
        IReadOnlyList<B09AuthSetCookie> refreshCookies;
        using (var refresh = await B09AuthHttp.PostNoBodyAsync(client, B09AuthSupport.RefreshEndpoint))
        {
            B09AuthSupport.AssertStatus(refresh, HttpStatusCode.NoContent, "refresh (TS-152)");
            refreshCookies = B09AuthSupport.ParseSetCookie(refresh);
        }

        // logout → 204, ровно 2 сброса.
        IReadOnlyList<B09AuthSetCookie> logoutCookies;
        using (var logout = await B09AuthHttp.PostNoBodyAsync(client, B09AuthSupport.LogoutEndpoint))
        {
            B09AuthSupport.AssertStatus(logout, HttpStatusCode.NoContent, "logout (TS-152)");
            logoutCookies = B09AuthSupport.ParseSetCookie(logout);
        }

        return (registerCookies, loginCookies, refreshCookies, logoutCookies);
    }
}
