using LabsApp.Auth;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-057 (P0, negative; FR-009) «Refresh: просроченный токен — 401 без
/// Set-Cookie».
/// given: Сессия создана в T0; инжектируемые часы переведены за expiresAt
///        (T0+7 дней+1 мс — TTL refresh 7 суток, expiresAt = T0+7 дней).
/// when:  POST /auth/refresh с refresh-cookie.
/// then:  401 'Не авторизован'; заголовков Set-Cookie нет (FR-009 AC
///        «Просроченный refresh»).
/// </summary>
public sealed class Ts057_RefreshExpiredPastExpiresAtTests(B11TimedWebAppFactory factory)
    : IClassFixture<B11TimedWebAppFactory>
{
    private readonly B11TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS057_Refresh_WithTokenPastExpiresAt_Returns401WithoutSetCookie()
    {
        // given: сессия создана в T0 (вход при фиктивном времени), refresh-токен
        // с expiresAt = T0+7 дней захвачен.
        using var client = HostClients.Create(_factory);
        using var login = await HostClients.LoginAsync(client, "teacher", "teacher123!");
        _ = await ApiAssert.ReadOkJsonAsync(login);

        var refreshValue = HostClients.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName)
            ?? throw new InvalidOperationException(
                "Вход не вернул Set-Cookie refresh_token — given кейса неисполним.");

        // given: часы переведены за expiresAt — на T0+7 дней+1 мс.
        _factory.Time.Advance(TimeSpan.FromDays(7).Add(TimeSpan.FromMilliseconds(1)));
        HostClients.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshValue);

        // when: POST /auth/refresh с refresh-cookie.
        using var response = await HostClients.PostWithoutBodyAsync(client, HostClients.RefreshPath);

        // then: 401 'Не авторизован'; заголовков Set-Cookie нет.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Unauthorized,
            "Не авторизован",
            exactSingleMessageProperty: true);
        Assert.False(response.Headers.Contains("Set-Cookie"), "401 на просроченный refresh не должен выставлять cookie.");
    }
}
