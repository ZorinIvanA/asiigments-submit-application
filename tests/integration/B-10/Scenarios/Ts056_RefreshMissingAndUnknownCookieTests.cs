using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-056 (P1, negative; FR-009) «Refresh: отсутствие и неизвестное значение
/// cookie — 401».
/// given: Хост запущен.
/// when:  POST /auth/refresh без cookie refresh_token; отдельно — со значением
///        refresh_token='garbage'.
/// then:  Оба — 401 'Не авторизован'; без Set-Cookie. FR-009 AC «Без cookie»;
///        ветка «токен неизвестен (нет записи по SHA-256)».
/// </summary>
public sealed class Ts056_RefreshMissingAndUnknownCookieTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.10.56";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS056_Refresh_WithoutCookieAndWithUnknownValue_Returns401WithoutSetCookie()
    {
        // given: хост запущен; запрос без cookie refresh_token.
        using var client = B10AuthRequests.Create(_factory, TestIp);

        // when (1): POST /auth/refresh без cookie.
        using var without = await B10AuthRequests.PostNoBodyAsync(client, B10CookieFlow.RefreshPath);

        // then (1): 401 'Не авторизован'; без Set-Cookie.
        await ApiAssert.AssertMessageAsync(
            without, HttpStatusCode.Unauthorized, "Не авторизован");
        Assert.Empty(B10AuthRequests.SetCookies(without));

        // when (2): POST /auth/refresh с refresh_token='garbage' (нет записи по SHA-256).
        B10AuthRequests.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, "garbage");
        using var garbage = await B10AuthRequests.PostNoBodyAsync(client, B10CookieFlow.RefreshPath);

        // then (2): 401 'Не авторизован'; без Set-Cookie.
        await ApiAssert.AssertMessageAsync(
            garbage, HttpStatusCode.Unauthorized, "Не авторизован");
        Assert.Empty(B10AuthRequests.SetCookies(garbage));
    }
}
