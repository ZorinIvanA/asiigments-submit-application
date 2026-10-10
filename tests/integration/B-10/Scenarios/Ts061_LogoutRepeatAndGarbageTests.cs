using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-061 (P2, idempotency; FR-010) «Logout: повтор и мусорное значение cookie
/// не отзывают чужие токены».
/// given: Logout уже выполнен (первый POST с refresh-cookie → 204); существует
///        валидная refresh-сессия ДРУГОГО пользователя (минт, ADR-015/ADR-022).
/// when:  Повторный POST /auth/logout с уже отозванным refresh-cookie; отдельно
///        logout со значением refresh_token='garbage'.
/// then:  Оба — 204; refresh-токен другого пользователя продолжает действовать —
///        его /auth/refresh — 204 (отзыв чужих/несуществующих токенов не
///        происходит, FR-010).
/// </summary>
public sealed class Ts061_LogoutRepeatAndGarbageTests(B10NoDemoWebAppFactory factory)
    : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS061_RepeatedLogout_AndGarbageCookie_KeepForeignRefreshAlive()
    {
        // given: два пользователя; refresh-токен первого будет отозван, второго —
        // остаётся живым контрольным значением (шаг given «валидная сессия другого»).
        var exiting = B10Seed.AddStudent(_factory, "b10ts061.exiting");
        var foreign = B10Seed.AddStudent(_factory, "b10ts061.foreign");
        var exitingRefresh = B10AuthSessions.MintRefreshToken(_factory, exiting.Id);
        var foreignRefresh = B10AuthSessions.MintRefreshToken(_factory, foreign.Id);
        using var client = HostClients.Create(_factory);

        // given: logout уже выполнен — первый выход отзывает предъявленный refresh.
        using var firstLogout = await PostWithRefreshCookieAsync(client, B10CookieFlow.LogoutPath, exitingRefresh);
        Assert.Equal(HttpStatusCode.NoContent, firstLogout.StatusCode);

        // when: повторный POST /auth/logout с тем же (уже отозванным) cookie.
        using var repeatedLogout = await PostWithRefreshCookieAsync(client, B10CookieFlow.LogoutPath, exitingRefresh);

        // when: logout со значением refresh_token='garbage'.
        using var garbageLogout = await PostWithRefreshCookieAsync(client, B10CookieFlow.LogoutPath, "garbage");

        // then: оба — 204.
        Assert.Equal(HttpStatusCode.NoContent, repeatedLogout.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, garbageLogout.StatusCode);

        // then: refresh-токен другого пользователя продолжает действовать — его
        // /auth/refresh — 204 (чужие/несуществующие токены не отозваны).
        using var foreignRefreshResponse = await PostWithRefreshCookieAsync(
            client, B10CookieFlow.RefreshPath, foreignRefresh);
        Assert.Equal(HttpStatusCode.NoContent, foreignRefreshResponse.StatusCode);
    }

    /// <summary>POST на путь с refresh-cookie (logout/refresh не требуют access, FR-010).</summary>
    private static async Task<HttpResponseMessage> PostWithRefreshCookieAsync(
        HttpClient client, string path, string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            $"{AuthCoreDefaults.RefreshTokenCookieName}={refreshToken}");
        return await client.SendAsync(request);
    }
}
