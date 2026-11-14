using LabsApp.Auth;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-058 (P0, negative; FR-009, FR-010) «Refresh: отозванный (после logout)
/// токен — 401».
/// given: Пользователь вошёл и выполнил POST /auth/logout (токен отозван).
/// when:  POST /auth/refresh с тем же refresh-токеном.
/// then:  401 'Не авторизован' (FR-009 AC «Отозванный refresh»).
/// </summary>
public sealed class Ts058_RefreshRevokedByLogoutTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS058_Refresh_WithTokenRevokedByLogout_Returns401()
    {
        // given: пользователь вошёл (refresh-токен захвачен) и выполнил
        // POST /auth/logout (токен отозван).
        using var client = HostClients.Create(_factory);
        using var login = await HostClients.LoginAsync(client, "teacher", "teacher123!");
        _ = await ApiAssert.ReadOkJsonAsync(login);

        var refreshValue = HostClients.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName)
            ?? throw new InvalidOperationException(
                "Вход не вернул Set-Cookie refresh_token — given кейса неисполним.");
        HostClients.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshValue);

        using var logout = await HostClients.PostWithoutBodyAsync(client, HostClients.LogoutPath);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // when: POST /auth/refresh с тем же refresh-токеном.
        using var response = await HostClients.PostWithoutBodyAsync(client, HostClients.RefreshPath);

        // then: 401 'Не авторизован' — logout отозвал токен (revokedAt≠null).
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Unauthorized,
            "Не авторизован",
            exactSingleMessageProperty: true);
    }
}
