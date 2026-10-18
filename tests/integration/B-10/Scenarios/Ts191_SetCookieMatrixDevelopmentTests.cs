using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-191 «NFR-007: матрица Set-Cookie 7/7 по экземплярам (Development)»
/// (nfr, NFR-007/FR-008, P0).
///
/// given: Development-стенд; HttpClient с отключенным авто-управлением cookie
///        для разбора сырых Set-Cookie.
/// when:  register (2 cookie), login (2), refresh (1), logout (2); разбор
///        каждого Set-Cookie.
/// then:  всего 7 экземпляров: register — access_token(Max-Age=900) +
///        refresh_token(604800); login — access_token(900) + refresh_token(604800);
///        refresh — ТОЛЬКО access_token(900); logout — оба с Max-Age=0;
///        HttpOnly, SameSite=Strict, Path=/ — 7 из 7; флаг Secure отсутствует
///        во всех 7 (Development) (NFR-007 verification).
/// </summary>
public sealed class Ts191_SetCookieMatrixDevelopmentTests : IClassFixture<B10NoDemoWebAppFactory>
{
    private const string FlowLogin = "b10ts191.user";
    private const string FlowPassword = "Passw0rd!";
    private const string FlowFullName = "Матрица Cookie Б10";

    private readonly B10NoDemoWebAppFactory _factory;

    public Ts191_SetCookieMatrixDevelopmentTests(B10NoDemoWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task AuthFlow_EmitsSevenSetCookieInstances_WithDevelopmentAttributes()
    {
        // given: HttpClient без cookie-контейнера — Set-Cookie разбираются сырыми.
        using var client = HostClients.Create(_factory);

        // when: register → login → refresh → logout; разбор каждого Set-Cookie.
        var steps = await B10CookieFlow.RunAsync(client, FlowLogin, FlowPassword, FlowFullName);

        // then: все шаги матрицы выполнены (2xx).
        Assert.All(steps, step => Assert.True(
            (int)step.Status is >= 200 and < 300,
            $"{step.Endpoint}: HTTP {(int)step.Status} — шаг матрицы Set-Cookie не выполнен."));

        // then: матрица 2+2+1+2 = 7 экземпляров с Max-Age по экземплярам.
        Assert.Collection(
            steps,
            register => AssertCookies(
                register,
                (AuthCoreDefaults.AccessTokenCookieName, "900"),
                (AuthCoreDefaults.RefreshTokenCookieName, "604800")),
            login => AssertCookies(
                login,
                (AuthCoreDefaults.AccessTokenCookieName, "900"),
                (AuthCoreDefaults.RefreshTokenCookieName, "604800")),
            refresh => AssertCookies(refresh, (AuthCoreDefaults.AccessTokenCookieName, "900")),
            logout => AssertCookies(
                logout,
                (AuthCoreDefaults.AccessTokenCookieName, "0"),
                (AuthCoreDefaults.RefreshTokenCookieName, "0")));

        // then: HttpOnly, SameSite=Strict, Path=/ — 7 из 7; Secure отсутствует во всех 7.
        var all = steps.SelectMany(step => step.Cookies).ToList();
        Assert.Equal(7, all.Count);
        foreach (var cookie in all)
        {
            Assert.True(cookie.HasFlag("httponly"), $"{cookie.Name}: нет флага HttpOnly.");
            // CR-001: значение атрибута сравнивается без учёта регистра — сериализацию
            // Set-Cookie определяет фреймворк (ASP.NET Core пишет «samesite=strict»);
            // семантика кейса — атрибут SameSite=Strict (как в TS-047).
            Assert.Equal("Strict", cookie.Attribute("samesite"), ignoreCase: true);
            Assert.Equal("/", cookie.Attribute("path"), ignoreCase: true);
            Assert.False(
                cookie.HasFlag("secure"),
                $"{cookie.Name}: флаг Secure недопустим в Development.");
        }
    }

    /// <summary>Шаг матрицы: состав cookie по именам и Max-Age по экземплярам.</summary>
    private static void AssertCookies(B10CookieFlowStep step, params (string Name, string MaxAge)[] expected)
    {
        Assert.True(
            step.Cookies.Count == expected.Length,
            $"{step.Endpoint}: ожидалось {expected.Length} Set-Cookie, фактически " +
            $"{step.Cookies.Count} ({string.Join(" | ", step.Cookies.Select(cookie => cookie.Name))}).");
        Assert.Equal(
            expected.Select(item => item.Name).OrderBy(name => name, StringComparer.Ordinal),
            step.Cookies.Select(cookie => cookie.Name).OrderBy(name => name, StringComparer.Ordinal));
        foreach (var (name, maxAge) in expected)
        {
            var cookie = Assert.Single(step.Cookies, item => item.Name == name);
            Assert.True(
                string.Equals(cookie.Attribute("max-age"), maxAge, StringComparison.Ordinal),
                $"{step.Endpoint}: {name} должен иметь Max-Age={maxAge}, фактически " +
                $"«{cookie.Attribute("max-age")}».");
        }
    }
}
