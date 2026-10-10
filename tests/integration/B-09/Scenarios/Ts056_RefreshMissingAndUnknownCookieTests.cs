using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-056 (P1, negative; FR-009) «Refresh: отсутствие и неизвестное значение
/// cookie — 401».
/// given: Хост запущен.
/// when:  POST /auth/refresh без cookie refresh_token; отдельно — со значением
///        refresh_token='garbage'.
/// then:  Оба — 401 'Не авторизован'; без Set-Cookie. FR-009 AC «Без cookie»;
///        ветка «токен неизвестен (нет записи по SHA-256)».
/// </summary>
public sealed class Ts056_RefreshMissingAndUnknownCookieTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.56";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS056_Refresh_WithoutCookieAndWithUnknownValue_Returns401WithoutSetCookie()
    {
        // given: хост запущен; запрос без cookie refresh_token.
        using var client = B09AuthHttp.Create(_factory, TestIp);

        // when (1): POST /auth/refresh без cookie.
        using var without = await B09AuthHttp.PostNoBodyAsync(client, B09AuthHttp.RefreshPath);

        // then (1): 401 'Не авторизован'; без Set-Cookie.
        var withoutBody = await B09Assertions.ParseObjectAsync(
            without, HttpStatusCode.Unauthorized, "TS-056: refresh без cookie");
        B09Assertions.MessageIs(withoutBody, "Не авторизован");
        Assert.Empty(B09AuthHttp.SetCookies(without));

        // when (2): POST /auth/refresh с refresh_token='garbage' (нет записи по SHA-256).
        B09AuthHttp.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, "garbage");
        using var garbage = await B09AuthHttp.PostNoBodyAsync(client, B09AuthHttp.RefreshPath);

        // then (2): 401 'Не авторизован'; без Set-Cookie.
        var garbageBody = await B09Assertions.ParseObjectAsync(
            garbage, HttpStatusCode.Unauthorized, "TS-056: refresh с неизвестным значением");
        B09Assertions.MessageIs(garbageBody, "Не авторизован");
        Assert.Empty(B09AuthHttp.SetCookies(garbage));
    }
}
