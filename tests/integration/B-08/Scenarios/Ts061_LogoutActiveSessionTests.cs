using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-061 «Выход с действующей сессией: 204, отзыв refresh, очистка cookie»
/// (happy_path, FR-015, P0).
///
/// given: вход выполнен, обе cookie установлены (DI-сид студента + минт сессии
///        через ITokenService/IRefreshTokenRepository — ADR-022).
/// when:  POST /api/v1/auth/logout; инспекция хранилища refresh-токенов;
///        повторный POST /api/v1/auth/refresh с прежним refresh.
/// then:  204; обе Set-Cookie с Max-Age=0; refresh в хранилище помечен revokedAt;
///        повторный /auth/refresh → 401 «Не авторизован».
///        FR-015 AC «Выход с действующей сессией».
/// </summary>
public sealed class Ts061_LogoutActiveSessionTests : IClassFixture<B08WebAppFactory>
{
    private const string Login = "ts061-student";
    private const string Email = "ts061@lab.local";
    private const string FullName = "Студент ШестьдесятОдин";

    private readonly B08WebAppFactory _factory;

    public Ts061_LogoutActiveSessionTests(B08WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Logout_WithActiveSession_ClearsCookiesRevokesRefreshAndRejectsReuse()
    {
        // given: вход выполнен (обе cookie установлены).
        using var client = B08Host.CreateClient(_factory);
        var user = B08Host.SeedStudent(_factory, Login, Email, FullName);
        var grant = B08Host.EstablishSession(_factory, client, user.Id, UserRoles.Student);
        Assert.True(client.Cookies.Contains(AuthCoreDefaults.AccessTokenCookieName), "Предусловие: access-cookie установлена.");
        Assert.True(client.Cookies.Contains(AuthCoreDefaults.RefreshTokenCookieName), "Предусловие: refresh-cookie установлена.");

        // when: POST /api/v1/auth/logout.
        using var logout = await client.PostAsync(B08Host.LogoutEndpoint, json: null);

        // then: 204.
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // then: обе Set-Cookie с Max-Age=0.
        ResponseAssertions.AssertCookieClearedByMaxAgeZero(logout, AuthCoreDefaults.AccessTokenCookieName);
        ResponseAssertions.AssertCookieClearedByMaxAgeZero(logout, AuthCoreDefaults.RefreshTokenCookieName);

        // then: refresh в хранилище помечен revokedAt.
        var stored = B08Host.FindRefreshRecordByHash(_factory, grant.TokenHash);
        Assert.NotNull(stored);
        Assert.True(
            stored.RevokedAt is not null,
            $"Ожидался отзыв refresh (RevokedAt != null), фактически RevokedAt={stored.RevokedAt?.ToString() ?? "null"}.");

        // when: повторный POST /auth/refresh с прежним refresh.
        using var refresh = await PostWithRawRefreshCookieAsync(client, grant.Value);

        // then: 401 «Не авторизован».
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        var body = await refresh.Content.ReadAsStringAsync();
        ResponseAssertions.AssertMessageEquals(body, AuthCoreDefaults.UnauthorizedMessage, "Повторный /auth/refresh");
    }

    /// <summary>
    /// Повторный refresh отправляет ПРЕЖНЕЕ значение refresh-cookie (кейс: «с
    /// прежним refresh») — значение вписывается в запрос напрямую, минуя
    /// контейнер, который уже очистил cookie по Max-Age=0 из ответа logout.
    /// </summary>
    private async Task<HttpResponseMessage> PostWithRawRefreshCookieAsync(B08Host.Client client, string refreshTokenValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, B08Host.RefreshEndpoint);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            $"{AuthCoreDefaults.RefreshTokenCookieName}={refreshTokenValue}");
        return await client.SendAsync(request);
    }
}
