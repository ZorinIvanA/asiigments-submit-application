using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-057 (P0, negative; FR-009) «Refresh: просроченный токен — 401 без
/// Set-Cookie» (актуальная нумерация кейсов батча B-10; родственный тест
/// предыдущей нумерации — Ts054_RefreshExpiredTokenTests).
/// given: Refresh выдан в T0 (expiresAt = T0+7 дней); инжектируемые часы
///        переведены на T0+7д+1с.
/// when:  POST /auth/refresh с этим refresh-cookie.
/// then:  401 'Не авторизован'; ответ не содержит Set-Cookie (AC FR-009
///        «Просроченный refresh»).
/// </summary>
public sealed class B10Ts057_RefreshExpiredTokenTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.20.57";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS057_Refresh_OneSecondPastSevenDayExpiry_Returns401WithoutSetCookie()
    {
        // given: refresh-cookie выпущена в T0 свежим входом (expiresAt = T0+7 дней,
        // TTL refresh Auth__RefreshTtlDays=7 суток).
        using var client = B10AuthRequests.Create(_factory, TestIp);
        using var login = await B10AuthRequests.LoginAsync(client, "teacher", B10AuthRequests.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var refreshCookie = B10AuthRequests.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName);
        Assert.False(string.IsNullOrEmpty(refreshCookie), "Вход не выдал refresh-cookie — given неисполним.");
        B10AuthRequests.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshCookie!);

        // when: инжектируемые часы переведены на T0+7д+1с — expiresAt в прошлом
        // (контракт: просрочен при expiresAt ≤ now).
        _factory.Time.Advance(TimeSpan.FromDays(7).Add(TimeSpan.FromSeconds(1)));
        using var response = await B10AuthRequests.PostNoBodyAsync(client, B10CookieFlow.RefreshPath);

        // then: 401 'Не авторизован'; ответ не содержит Set-Cookie.
        await ApiAssert.AssertMessageAsync(
            response, HttpStatusCode.Unauthorized, "Не авторизован");
        Assert.Empty(B10AuthRequests.SetCookies(response));
    }
}
