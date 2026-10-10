using LabsApp.Auth;
using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-057 «Отозванный logout'ом refresh: 401» (negative, FR-014, P0).
///
/// given: refresh отозван logout'ом.
/// when:  POST /auth/refresh с этой refresh-cookie.
/// then:  401 «Не авторизован». FR-014 AC «Отозванный refresh».
///
/// Доработка CR-001: клиент БЕЗ cookie-контейнера (по образцу TS-056) — logout
/// отвечает ClearAuth (обе cookie Max-Age=0), контейнер вычистил бы refresh_token,
/// и запрос ушёл бы по ветке «cookie отсутствует» (кейс TS-059). Здесь значение
/// refresh-cookie перехватывается из Set-Cookie ДО logout, logout выполняется с
/// явными Cookie-заголовками, затем прежнее значение предъявляется ЯВНО. Факт
/// отзыва фиксируется ПОВЕДЕНЧЕСКИ: refresh с отозванной cookie → 401 (неотозванный
/// живой токен дал бы 204, FR-009/ADR-010); истечение исключено — токен свежий,
/// время не сдвигалось. Прямая инспекция RevokedAt записи невозможна: единственный
/// поиск по хэшу в консолидированном ISecurityTokenRepository — FindLiveByHash,
/// отфильтровывающий неживые (отозванные) записи (IF-015/ADR-030).
/// </summary>
public sealed class Ts057_RefreshRevokedByLogoutTests : IClassFixture<B07AuthWebAppFactory>
{
    private readonly B07AuthWebAppFactory _factory;

    public Ts057_RefreshRevokedByLogoutTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RefreshWithRevokedByLogoutCookie_Returns401Unauthorized()
    {
        // given: действующий refresh после входа; значения cookie перехвачены
        // из Set-Cookie (клиент без cookie-контейнера — кейс сам управляет заголовками).
        using var client = B07AuthClients.CreateClientWithoutCookies(_factory);
        using var login = await B07AuthClients.PostLoginAsync(client, "teacher", B07AuthWebAppFactory.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var access = B07AuthClients.RequiredSetCookie(login, AuthCoreDefaults.AccessTokenCookieName);
        var refreshToken = B07AuthClients.RequiredSetCookie(login, AuthCoreDefaults.RefreshTokenCookieName);

        // given (предусловие): запись в хранилище жива — отзыва ещё нет.
        Assert.Null(B07AuthClients.FindRefreshToken(_factory, refreshToken).RevokedAt);

        // given: refresh отозван logout'ом — logout с явными Cookie-заголовками
        // обеих cookie (браузер присылает обе; контракт: выполнение при любой из cookie).
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, B07AuthClients.LogoutEndpoint);
        logoutRequest.Headers.TryAddWithoutValidation(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={access}; {AuthCoreDefaults.RefreshTokenCookieName}={refreshToken}");
        using var logout = await client.SendAsync(logoutRequest);
        Assert.True(
            logout.StatusCode == HttpStatusCode.NoContent,
            $"Предусловие кейса: logout должен отозвать refresh, фактически {(int)logout.StatusCode}: {await logout.Content.ReadAsStringAsync()}");

        // Факт отзыва проверяется поведенчески ниже: неотозванный живой токен
        // дал бы refresh 204 (FR-009), а не 401.

        // when: POST /auth/refresh с ЭТОЙ (отозванной) refresh-cookie — прежнее
        // значение предъявлено явно, независимо от ClearAuth-ответа logout'а.
        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, B07AuthClients.RefreshEndpoint);
        refreshRequest.Headers.TryAddWithoutValidation(
            "Cookie",
            $"{AuthCoreDefaults.RefreshTokenCookieName}={refreshToken}");
        using var refresh = await client.SendAsync(refreshRequest);

        // then: 401 «Не авторизован».
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(refresh);
        BodyAssertions.MessageIs(root, "Не авторизован");
    }
}
