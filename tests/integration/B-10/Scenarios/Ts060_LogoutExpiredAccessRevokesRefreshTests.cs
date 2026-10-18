using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-060 (P0, happy_path; FR-010) «Logout: истёкший access + живой refresh —
/// refresh отозван».
/// given: Access-токен истёк (токен с exp на 15 минут в прошлом, подписан ключом
///        стенда — вариант given «истёкший access») либо access-cookie отсутствует
///        (второй допустимый вариант given); refresh валиден (минт через
///        ITokenService + ISecurityTokenRepository, B10AuthSessions).
/// when:  POST /auth/logout с refresh-cookie (без валидного access).
/// then:  204 и refresh отозван, несмотря на невалидный/отсутствующий access —
///        последующий /auth/refresh с тем же refresh-cookie — 401 «Не авторизован»
///        (FR-010 AC «Истёкший access + живой refresh»).
/// </summary>
public sealed class Ts060_LogoutExpiredAccessRevokesRefreshTests(B10NoDemoWebAppFactory factory)
    : IClassFixture<B10NoDemoWebAppFactory>
{
    /// <summary>Сдвиг exp истёкшего access-токена в прошлое (given: «за 15 минут»).</summary>
    private static readonly TimeSpan ExpiredFor = TimeSpan.FromMinutes(15);

    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS060_Logout_WithExpiredAccessCookie_RevokesLiveRefresh()
    {
        // given: владелец refresh-сессии; access-токен с истёкшим exp (ключ стенда);
        // refresh-токен жив (минт, ADR-015/ADR-022).
        var owner = B10Seed.AddStudent(_factory, "b10ts060.expired");
        var expiredAccess = B10AuthSessions.CraftAccessToken(
            owner.Id,
            UserRoles.Student,
            issuedAt: DateTimeOffset.UtcNow - ExpiredFor - TimeSpan.FromMinutes(1),
            expiresAt: DateTimeOffset.UtcNow - ExpiredFor,
            signingKey: B10HostFactory.TestJwtKey);
        var refreshToken = B10AuthSessions.MintRefreshToken(_factory, owner.Id);
        using var client = HostClients.Create(_factory);

        // when: POST /auth/logout с истёкшим access-cookie и живым refresh-cookie.
        using var logoutResponse = await PostWithCookiesAsync(
            client,
            B10CookieFlow.LogoutPath,
            (AuthCoreDefaults.AccessTokenCookieName, expiredAccess),
            (AuthCoreDefaults.RefreshTokenCookieName, refreshToken));

        // then: 204.
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // then: refresh отозван — последующий /auth/refresh с тем же значением — 401.
        await AssertRefreshUnauthorizedAsync(client, refreshToken);
    }

    [Fact]
    public async Task TS060_Logout_WithoutAccessCookie_RevokesLiveRefresh()
    {
        // given: владелец refresh-сессии; access-cookie отсутствует (второй вариант
        // given «без валидного access»); refresh-токен жив.
        var owner = B10Seed.AddStudent(_factory, "b10ts060.noaccess");
        var refreshToken = B10AuthSessions.MintRefreshToken(_factory, owner.Id);
        using var client = HostClients.Create(_factory);

        // when: POST /auth/logout только с refresh-cookie.
        using var logoutResponse = await PostWithCookiesAsync(
            client,
            B10CookieFlow.LogoutPath,
            (AuthCoreDefaults.RefreshTokenCookieName, refreshToken));

        // then: 204.
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // then: refresh отозван — последующий /auth/refresh — 401 «Не авторизован».
        await AssertRefreshUnauthorizedAsync(client, refreshToken);
    }

    /// <summary>POST по пути с явными парами cookie (cookie-контейнер не используется).</summary>
    private static async Task<HttpResponseMessage> PostWithCookiesAsync(
        HttpClient client,
        string path,
        params (string Name, string Value)[] cookies)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}")));
        return await client.SendAsync(request);
    }

    /// <summary>then «последующий /auth/refresh — 401 'Не авторизован'».</summary>
    private static async Task AssertRefreshUnauthorizedAsync(HttpClient client, string refreshToken)
    {
        using var refreshResponse = await PostWithCookiesAsync(
            client,
            B10CookieFlow.RefreshPath,
            (AuthCoreDefaults.RefreshTokenCookieName, refreshToken));
        await ApiAssert.AssertMessageAsync(
            refreshResponse,
            HttpStatusCode.Unauthorized,
            "Не авторизован");
    }
}
