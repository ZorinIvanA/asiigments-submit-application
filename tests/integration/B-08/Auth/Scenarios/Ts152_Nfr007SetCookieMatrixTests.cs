using LabsApp.IntegrationTests.B08.Auth.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Auth.Scenarios;

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
/// </summary>
public sealed class Ts152_Nfr007SetCookieMatrixTests
{
    private const string LoginName = "b08matrix";
    private const string Email = "b08matrix@example.com";
    private const string FullName = "Матрица Куки";
    private const string Password = "Passw0rd!";

    [Fact]
    public async Task DevelopmentHost_SevenSetCookieInstances_FullMatrixWithoutSecure()
    {
        var (register, login, refresh, logout) = await RunFourEndpointFlow(new B08AuthDevFactory());

        AssertInstanceMatrix(register, login, refresh, logout, expectSecure: false);
    }

    [Fact]
    public async Task ProductionHost_SevenSetCookieInstances_SecureOnAllSeven()
    {
        var (register, login, refresh, logout) = await RunFourEndpointFlow(new B08AuthProductionFactory());

        AssertInstanceMatrix(register, login, refresh, logout, expectSecure: true);
    }

    /// <summary>Матрица NFR-007 по экземплярам: состав, атрибуты, Max-Age, Secure.</summary>
    private static void AssertInstanceMatrix(
        IReadOnlyList<B08AuthSetCookie> register,
        IReadOnlyList<B08AuthSetCookie> login,
        IReadOnlyList<B08AuthSetCookie> refresh,
        IReadOnlyList<B08AuthSetCookie> logout,
        bool expectSecure)
    {
        // then: всего 7 инстансов: register (2), login (2), refresh (1), logout (2).
        Assert.Equal(2, register.Count);
        Assert.Equal(2, login.Count);
        Assert.Single(refresh);
        Assert.Equal(2, logout.Count);

        var environment = expectSecure ? "Production" : "Development";

        // then: состав пар «имя → Max-Age» по эндпойнтам.
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
    /// одном клиенте; возвращает экземпляры Set-Cookie каждого ответа.
    /// </summary>
    private static async Task<(
        IReadOnlyList<B08AuthSetCookie> Register,
        IReadOnlyList<B08AuthSetCookie> Login,
        IReadOnlyList<B08AuthSetCookie> Refresh,
        IReadOnlyList<B08AuthSetCookie> Logout)> RunFourEndpointFlow(B08AuthWebAppFactory factory)
    {
        using var client = B08AuthHost.CreateClient(factory);

        // register → 201, ровно 2 Set-Cookie.
        using (var register = await client.PostAsync(
            B08AuthHost.RegisterEndpoint,
            "{\"fullName\":\"" + FullName + "\",\"login\":\"" + LoginName + "\",\"email\":\"" + Email + "\"," +
            "\"password\":\"" + Password + "\",\"repeatPassword\":\"" + Password + "\"}"))
        {
            B08AuthHost.AssertStatus(register, HttpStatusCode.Created, "register (TS-152)");
            var registerCookies = B08AuthHost.ParseSetCookie(register);

            // login → 200, ровно 2 Set-Cookie (контейнер клиента уже несёт cookie
            // регистрации — вход по ним не зависит, тело решает).
            using (var login = await client.PostAsync(
                B08AuthHost.LoginEndpoint,
                "{\"login\":\"" + LoginName + "\",\"password\":\"" + Password + "\"}"))
            {
                B08AuthHost.AssertStatus(login, HttpStatusCode.OK, "login (TS-152)");
                var loginCookies = B08AuthHost.ParseSetCookie(login);

                // refresh → 204, ровно 1 Set-Cookie (по refresh-cookie из контейнера).
                using (var refresh = await client.PostAsync(B08AuthHost.RefreshEndpoint, json: null))
                {
                    B08AuthHost.AssertStatus(refresh, HttpStatusCode.NoContent, "refresh (TS-152)");
                    var refreshCookies = B08AuthHost.ParseSetCookie(refresh);

                    // logout → 204, ровно 2 сброса.
                    using (var logout = await client.PostAsync(B08AuthHost.LogoutEndpoint, json: null))
                    {
                        B08AuthHost.AssertStatus(logout, HttpStatusCode.NoContent, "logout (TS-152)");
                        var logoutCookies = B08AuthHost.ParseSetCookie(logout);

                        return (registerCookies, loginCookies, refreshCookies, logoutCookies);
                    }
                }
            }
        }
    }
}
