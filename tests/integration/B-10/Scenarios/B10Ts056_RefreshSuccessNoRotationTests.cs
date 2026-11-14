using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-056 (P0, happy_path; FR-009) «Refresh: успех — 204, новый access, refresh не
/// переустанавливается» (актуальная нумерация кейсов батча B-10; родственный тест
/// предыдущей нумерации — Ts053_RefreshSuccessTests).
/// given: Валидная refresh-cookie (свежий вход); исходное значение refresh
///        сохранено тестом.
/// when:  POST /auth/refresh; затем повторный POST /auth/refresh с исходным
///        refresh.
/// then:  204; Set-Cookie нового access_token; Set-Cookie refresh_token в ответе
///        ОТСУТСТВУЕТ (refresh не перевыпускается и не ротируется — ASM-003);
///        исходный refresh остаётся валиден (повторный refresh — 204) (AC FR-009
///        «Успешный refresh»).
/// </summary>
public sealed class B10Ts056_RefreshSuccessNoRotationTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.20.56";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS056_Refresh_WithLiveRefreshCookie_Returns204NewAccessAndKeepsOriginalRefreshValid()
    {
        // given: валидная refresh-cookie свежего входа; исходное значение refresh
        // сохранено тестом (часы сдвигаются явно — access-токены последовательных
        // выпусков различимы по iat).
        using var client = B10AuthRequests.Create(_factory, TestIp);
        using var login = await B10AuthRequests.LoginAsync(client, "teacher", B10AuthRequests.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var accessAtLogin = B10AuthRequests.SetCookieValue(login, AuthCoreDefaults.AccessTokenCookieName);
        var originalRefresh = B10AuthRequests.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName);
        Assert.False(string.IsNullOrEmpty(originalRefresh), "Вход не выдал refresh-cookie — given неисполним.");
        B10AuthRequests.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, originalRefresh!);

        // when: POST /auth/refresh.
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        using var first = await B10AuthRequests.PostNoBodyAsync(client, B10CookieFlow.RefreshPath);

        // then: 204; Set-Cookie нового access_token; Set-Cookie refresh_token в
        // ответе ОТСУТСТВУЕТ (не перевыпускается и не ротируется — ASM-003).
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        var firstAccess = B10AuthRequests.SetCookieValue(first, AuthCoreDefaults.AccessTokenCookieName);
        Assert.True(firstAccess is not null, "Refresh не выпустил Set-Cookie access_token.");
        Assert.NotEqual(accessAtLogin, firstAccess);
        Assert.False(
            B10AuthRequests.HasSetCookie(first, AuthCoreDefaults.RefreshTokenCookieName),
            "Refresh не должен переустанавливать refresh_token.");

        // when: повторный POST /auth/refresh с ИСХОДНЫМ значением refresh.
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        B10AuthRequests.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, originalRefresh!);
        using var second = await B10AuthRequests.PostNoBodyAsync(client, B10CookieFlow.RefreshPath);

        // then: исходный refresh остаётся валиден — снова 204 с новым access и без
        // Set-Cookie refresh_token.
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var secondAccess = B10AuthRequests.SetCookieValue(second, AuthCoreDefaults.AccessTokenCookieName);
        Assert.True(secondAccess is not null, "Повторный refresh не выпустил Set-Cookie access_token.");
        Assert.NotEqual(firstAccess, secondAccess);
        Assert.False(
            B10AuthRequests.HasSetCookie(second, AuthCoreDefaults.RefreshTokenCookieName),
            "Повторный refresh не должен переустанавливать/ротировать refresh_token.");
    }
}
