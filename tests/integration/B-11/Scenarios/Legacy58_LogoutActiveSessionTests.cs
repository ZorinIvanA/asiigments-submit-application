using LabsApp.Auth;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// Legacy58 (P0, happy_path; FR-010, NFR-007) «Logout: валидная сессия — 204,
/// refresh отозван, cookie сброшены». Кейс СТАРОГО реестра зоны (бывший TS-058),
/// сохранён вне актуального реестра батча (TS-041..TS-050 / TS-056..TS-060) под
/// однозначным ID LegacyNN — logout новым батчем не покрывается (CR-001 раунда
/// 2026-10-10). Разбор Set-Cookie — общий хелпер зоны HostClients (CR-002).
/// given: Пользователь вошёл (оба cookie установлены).
/// when:  POST /auth/logout.
/// then:  204; refresh-токен отозван (последующий /auth/refresh — 401); оба
///        cookie сброшены (Set-Cookie с Max-Age=0) (FR-010 AC «Выход с валидной
///        сессией»).
/// </summary>
public sealed class Legacy58_LogoutActiveSessionTests(B11WebAppFactory factory) : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task Legacy58_Logout_WithActiveSession_Returns204ClearsBothCookiesAndRevokesRefresh()
    {
        // given: пользователь вошёл (оба cookie установлены; значения зафиксированы).
        using var client = HostClients.Create(_factory);
        using var login = await HostClients.LoginAsync(client, "teacher", "teacher123!");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var accessToken = HostClients.SetCookieValue(login, AuthCoreDefaults.AccessTokenCookieName);
        var refreshCookie = HostClients.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName);
        Assert.False(string.IsNullOrEmpty(accessToken), "Вход не выдал access-cookie — given неисполним.");
        Assert.False(string.IsNullOrEmpty(refreshCookie), "Вход не выдал refresh-cookie — given неисполним.");
        SetRequestCookies(
            client,
            (AuthCoreDefaults.AccessTokenCookieName, accessToken!),
            (AuthCoreDefaults.RefreshTokenCookieName, refreshCookie!));

        // when: POST /auth/logout.
        using var response = await HostClients.PostWithoutBodyAsync(client, HostClients.LogoutPath);

        // then: 204; оба cookie сброшены — Set-Cookie с Max-Age=0 (NFR-007: logout —
        // ровно 2 сброса; атрибут сверяется без учёта регистра — общий хелпер зоны).
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var accessClear = HostClients.SetCookieHeader(response, AuthCoreDefaults.AccessTokenCookieName)
            ?? throw new InvalidOperationException("Logout не сбросил cookie access_token.");
        Assert.True(
            HostClients.HasSetCookieAttribute(accessClear, "max-age=0"),
            $"access_token сброшен без Max-Age=0: {accessClear}");
        var refreshClear = HostClients.SetCookieHeader(response, AuthCoreDefaults.RefreshTokenCookieName)
            ?? throw new InvalidOperationException("Logout не сбросил cookie refresh_token.");
        Assert.True(
            HostClients.HasSetCookieAttribute(refreshClear, "max-age=0"),
            $"refresh_token сброшен без Max-Age=0: {refreshClear}");

        // then: refresh-токен отозван — последующий /auth/refresh с прежним
        // значением — 401 'Не авторизован'.
        using var after = await HostClients.PostWithoutBodyAsync(client, HostClients.RefreshPath);
        await ApiAssert.AssertMessageAsync(
            after,
            HttpStatusCode.Unauthorized,
            "Не авторизован",
            exactSingleMessageProperty: true);
    }

    /// <summary>Заголовок Cookie клиента = перечисленные пары name=value.</summary>
    private static void SetRequestCookies(HttpClient client, params (string Name, string Value)[] cookies)
    {
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));
    }
}
