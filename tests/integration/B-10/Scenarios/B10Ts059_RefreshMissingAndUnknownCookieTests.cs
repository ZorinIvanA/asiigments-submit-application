using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-059 (P1, negative; FR-009) «Refresh: без cookie и с неизвестным токеном —
/// 401» (актуальная нумерация кейсов батча B-10; родственный тест предыдущей
/// нумерации — Ts056_RefreshMissingAndUnknownCookieTests).
/// given: Два запроса подготовлены: без cookie refresh_token; со значением
///        'garbage' (нет записи по SHA-256).
/// when:  POST /auth/refresh без cookie; затем со значением 'garbage'.
/// then:  Оба — 401 'Не авторизован'; без Set-Cookie (AC FR-009 «Без cookie»;
///        FR-009: «неизвестен (нет записи по SHA-256) → 401»).
/// </summary>
public sealed class B10Ts059_RefreshMissingAndUnknownCookieTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.20.59";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS059_Refresh_WithoutCookieAndWithUnknownValue_Returns401WithoutSetCookie()
    {
        // given: хост запущен; запрос без cookie refresh_token подготовлен.
        using var client = B10AuthRequests.Create(_factory, TestIp);

        // when (1): POST /auth/refresh без cookie.
        using var without = await B10AuthRequests.PostNoBodyAsync(client, B10CookieFlow.RefreshPath);

        // then (1): 401 'Не авторизован'; без Set-Cookie.
        await ApiAssert.AssertMessageAsync(
            without, HttpStatusCode.Unauthorized, "Не авторизован");
        Assert.Empty(B10AuthRequests.SetCookies(without));

        // when (2): POST /auth/refresh со значением 'garbage' (нет записи по SHA-256).
        B10AuthRequests.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, "garbage");
        using var garbage = await B10AuthRequests.PostNoBodyAsync(client, B10CookieFlow.RefreshPath);

        // then (2): 401 'Не авторизован'; без Set-Cookie.
        await ApiAssert.AssertMessageAsync(
            garbage, HttpStatusCode.Unauthorized, "Не авторизован");
        Assert.Empty(B10AuthRequests.SetCookies(garbage));
    }
}
