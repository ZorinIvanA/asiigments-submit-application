using System.Net;
using System.Text.RegularExpressions;
using LabsApp.Auth;
using LabsApp.Tests.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.Tests.Auth;

// ============================================================================
// NFR-007 (ISS-010/AR-001): консолидированная cookie-матрица dev-конфигурации.
// Сквозной сценарий register → login → refresh → logout на одном хосте
// (Development) собирает ВСЕ Set-Cookie четырёх эндпойнтов и разбирает их
// по-экземплярно: ровно 7 (register 2, login 2, refresh ТОЛЬКО access,
// logout 2 сброса); атрибуты HttpOnly/SameSite=Strict/Path=/ — 7 из 7;
// Max-Age — 900 у всех трёх выпусков access, 604800 у обоих выпусков refresh,
// 0 у обоих сбросов logout; Secure отсутствует во всех семи (Development).
//
// Атрибуты cookie в юнит-изолированном виде (включая Production Secure 7/7)
// проверяет CookieServiceTests; этот класс замыкает интеграционную часть
// матрицы — суммарное число и соответствие экземпляров эндпойнтам (FR-008).
// Хост общий с остальными auth-эндпойнтами (коллекция AuthEndpoints): окно
// регистраций сдвигается перед сценарием, уникальный логин исключает ключи
// лимитера других тестов.
// ============================================================================

/// <summary>Матрица Set-Cookie четырёх auth-эндпойнтов (NFR-007, Development).</summary>
[Collection("AuthEndpoints")]
public sealed class AuthCookieMatrixTests(AuthApiFactory fixture)
{
    private readonly AuthApiFactory _fixture = fixture;

    private WebApplicationFactory<Program> Factory => _fixture.Factory;

    [Fact]
    public async Task CookieMatrix_DevConfiguration_ExactlySevenSetCookies_WithNfr007Attributes()
    {
        // given: чистое окно регистраций; ручной контейнер сквозного сценария
        // (TestServer-клиент без автопереноса cookie, ADR-010).
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        using var client = AuthEndpointHarness.CreateClient(Factory);
        var container = new TestCookieContainer();
        var allSetCookies = new List<string>();

        // when/then: register — ровно 2 Set-Cookie: access (900) + refresh (604800).
        using var registered = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            """{"fullName":"Матрица Cookie","login":"cookie-matrix","email":"cookie-matrix@example.com","password":"Passw0rd!","repeatPassword":"Passw0rd!"}""");
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var registerCookies = AuthEndpointHarness.SetCookies(registered);
        Assert.Equal(2, registerCookies.Length);
        AuthEndpointHarness.AssertAuthCookieAttributes(
            AuthEndpointHarness.SetCookieOf(registerCookies, AuthCoreDefaults.AccessTokenCookieName),
            "900");
        AuthEndpointHarness.AssertAuthCookieAttributes(
            AuthEndpointHarness.SetCookieOf(registerCookies, AuthCoreDefaults.RefreshTokenCookieName),
            "604800");
        allSetCookies.AddRange(registerCookies);
        container.CaptureFrom(registered);

        // when/then: login — ровно 2 Set-Cookie: access (900) + refresh (604800).
        using var loggedIn = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            """{"login":"cookie-matrix","password":"Passw0rd!"}""");
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        var loginCookies = AuthEndpointHarness.SetCookies(loggedIn);
        Assert.Equal(2, loginCookies.Length);
        AuthEndpointHarness.AssertAuthCookieAttributes(
            AuthEndpointHarness.SetCookieOf(loginCookies, AuthCoreDefaults.AccessTokenCookieName),
            "900");
        AuthEndpointHarness.AssertAuthCookieAttributes(
            AuthEndpointHarness.SetCookieOf(loginCookies, AuthCoreDefaults.RefreshTokenCookieName),
            "604800");
        allSetCookies.AddRange(loginCookies);
        container.CaptureFrom(loggedIn);

        // when/then: refresh — ровно 1 Set-Cookie, ТОЛЬКО access (900);
        // refresh_token не переустанавливается (FR-009, без ротации).
        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, AuthEndpointHarness.RefreshEndpoint);
        container.ApplyTo(refreshRequest);
        using var refreshed = await client.SendAsync(refreshRequest);
        Assert.Equal(HttpStatusCode.NoContent, refreshed.StatusCode);
        var refreshCookies = AuthEndpointHarness.SetCookies(refreshed);
        Assert.Single(refreshCookies);
        AuthEndpointHarness.AssertAuthCookieAttributes(
            AuthEndpointHarness.SetCookieOf(refreshCookies, AuthCoreDefaults.AccessTokenCookieName),
            "900");
        allSetCookies.AddRange(refreshCookies);

        // when/then: logout — ровно 2 Set-Cookie, оба сброса (Max-Age=0).
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, AuthEndpointHarness.LogoutEndpoint);
        container.ApplyTo(logoutRequest);
        using var loggedOut = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);
        var logoutCookies = AuthEndpointHarness.SetCookies(loggedOut);
        Assert.Equal(2, logoutCookies.Length);
        AuthEndpointHarness.AssertAuthCookieAttributes(
            AuthEndpointHarness.SetCookieOf(logoutCookies, AuthCoreDefaults.AccessTokenCookieName),
            "0");
        AuthEndpointHarness.AssertAuthCookieAttributes(
            AuthEndpointHarness.SetCookieOf(logoutCookies, AuthCoreDefaults.RefreshTokenCookieName),
            "0");
        allSetCookies.AddRange(logoutCookies);

        // then: матрица целиком (NFR-007): ровно 7 Set-Cookie на 4 эндпойнтах;
        // атрибуты HttpOnly/SameSite=Strict/Path=/ и отсутствие Secure — 7 из 7
        // (Development); Max-Age — 900 у всех 3 выпусков access, 604800 у обоих
        // выпусков refresh, 0 у обоих сбросов logout.
        Assert.Equal(7, allSetCookies.Count);
        Assert.All(allSetCookies, setCookie =>
            AuthEndpointHarness.AssertAuthCookieAttributes(setCookie, MaxAgeOf(setCookie)));
        Assert.Equal(3, allSetCookies.Count(setCookie => MaxAgeOf(setCookie) == "900"));
        Assert.Equal(2, allSetCookies.Count(setCookie => MaxAgeOf(setCookie) == "604800"));
        Assert.Equal(2, allSetCookies.Count(setCookie => MaxAgeOf(setCookie) == "0"));
    }

    /// <summary>Значение Max-Age из Set-Cookie (атрибут обязателен у auth-cookie).</summary>
    private static string MaxAgeOf(string setCookie)
    {
        var match = Regex.Match(setCookie, "max-age=(\\d+)", RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"В Set-Cookie отсутствует Max-Age: {setCookie}");
        return match.Groups[1].Value;
    }
}
