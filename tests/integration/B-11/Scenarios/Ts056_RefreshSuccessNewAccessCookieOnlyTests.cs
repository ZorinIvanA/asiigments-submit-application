using LabsApp.Auth;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-056 (P0, happy_path; FR-009, NFR-007) «Refresh: успех 204, перевыпуск
/// только access».
/// given: Валидная refresh-cookie, полученная при входе.
/// when:  POST /api/v1/auth/refresh.
/// then:  204; Set-Cookie нового access_token (Max-Age=900, HttpOnly,
///        SameSite=Strict, Path=/); refresh_token НЕ переустановлен (нет
///        Set-Cookie refresh_token) (FR-009 AC «Успешный refresh»).
/// </summary>
public sealed class Ts056_RefreshSuccessNewAccessCookieOnlyTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS056_Refresh_WithValidCookie_Returns204AndSetsOnlyNewAccessCookie()
    {
        // given: валидная refresh-cookie, полученная при входе.
        using var client = HostClients.Create(_factory);
        using var login = await HostClients.LoginAsync(client, "teacher", "teacher123!");
        _ = await ApiAssert.ReadOkJsonAsync(login);

        var refreshValue = HostClients.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName)
            ?? throw new InvalidOperationException(
                "Вход не вернул Set-Cookie refresh_token — given кейса неисполним.");
        HostClients.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshValue);

        // when: POST /api/v1/auth/refresh.
        using var response = await HostClients.PostWithoutBodyAsync(client, HostClients.RefreshPath);

        // then: 204.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // then: Set-Cookie нового access_token с атрибутами поэкземпляру
        // (NFR-007; сверка атрибутов без учёта регистра — общим хелпером зоны
        // HostClients.HasSetCookieAttribute, CR-002; Development — Secure
        // отсутствует).
        var accessSetCookie = HostClients.SetCookieHeader(response, AuthCoreDefaults.AccessTokenCookieName)
            ?? throw new InvalidOperationException(
                "Refresh не установил новый cookie access_token.");
        Assert.True(HostClients.HasSetCookieAttribute(accessSetCookie, "max-age=900"), $"access_token без Max-Age=900: {accessSetCookie}");
        Assert.True(HostClients.HasSetCookieAttribute(accessSetCookie, "httponly"), $"access_token без HttpOnly: {accessSetCookie}");
        Assert.True(HostClients.HasSetCookieAttribute(accessSetCookie, "samesite=strict"), $"access_token без SameSite=Strict: {accessSetCookie}");
        Assert.True(HostClients.HasSetCookieAttribute(accessSetCookie, "path=/"), $"access_token без Path=/: {accessSetCookie}");

        // then: refresh_token НЕ переустановлен (нет Set-Cookie refresh_token).
        Assert.False(
            HostClients.HasSetCookie(response, AuthCoreDefaults.RefreshTokenCookieName),
            "Refresh не должен переустанавливать refresh_token.");
    }
}
