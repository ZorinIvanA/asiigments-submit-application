using LabsApp.Auth;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// Legacy61 (P2, idempotency; FR-010) «Logout: повтор и мусорное значение cookie
/// не отзывают чужие токены». Кейс СТАРОГО реестра зоны (бывший TS-061), сохранён
/// вне актуального реестра батча (TS-041..TS-050 / TS-056..TS-060) под однозначным
/// ID LegacyNN — logout новым батчем не покрывается, а номер TS-061 оставлен
/// свободным для будущих канонических реестров (CR-001 раунда 2026-10-10).
/// given: Logout первого пользователя уже выполнен; существует валидная сессия
///        ДРУГОГО пользователя (оба — DI-сид-студенты, вход по HTTP).
/// when:  Повторный POST /auth/logout первого; отдельно — logout со значением
///        refresh_token='garbage'.
/// then:  Оба — 204; refresh-токен другого пользователя продолжает действовать
///        (его /auth/refresh — 204) — отзыв чужих/несуществующих токенов не
///        происходит (FR-010).
/// </summary>
public sealed class Legacy61_LogoutRepeatGarbageKeepForeignTokensTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task Legacy61_RepeatedLogout_AndGarbageCookie_KeepForeignRefreshValid()
    {
        // given: валидные сессии двух разных студентов.
        B11RecoverySeed.AddStudent(
            _factory, "b11ts061.alice", "b11ts061.alice@example.com", "Parol1234!");
        B11RecoverySeed.AddStudent(
            _factory, "b11ts061.bob", "b11ts061.bob@example.com", "Parol1234!");

        using var clientA = HostClients.Create(_factory);
        using var loginA = await HostClients.LoginAsync(clientA, "b11ts061.alice", "Parol1234!");
        _ = await ApiAssert.ReadOkJsonAsync(loginA);
        var refreshA = HostClients.SetCookieValue(loginA, AuthCoreDefaults.RefreshTokenCookieName)
            ?? throw new InvalidOperationException(
                "Вход A не вернул Set-Cookie refresh_token — given кейса неисполним.");

        using var clientB = HostClients.Create(_factory);
        using var loginB = await HostClients.LoginAsync(clientB, "b11ts061.bob", "Parol1234!");
        _ = await ApiAssert.ReadOkJsonAsync(loginB);
        var refreshB = HostClients.SetCookieValue(loginB, AuthCoreDefaults.RefreshTokenCookieName)
            ?? throw new InvalidOperationException(
                "Вход B не вернул Set-Cookie refresh_token — given кейса неисполним.");

        // given: logout первого пользователя уже выполнен (токен A отозван).
        using var firstLogoutClient = HostClients.Create(_factory);
        HostClients.SetRequestCookie(firstLogoutClient, AuthCoreDefaults.RefreshTokenCookieName, refreshA);
        using var firstLogout =
            await HostClients.PostWithoutBodyAsync(firstLogoutClient, HostClients.LogoutPath);
        Assert.Equal(HttpStatusCode.NoContent, firstLogout.StatusCode);

        // when: повторный POST /auth/logout (та же уже отозванная cookie).
        using var repeatClient = HostClients.Create(_factory);
        HostClients.SetRequestCookie(repeatClient, AuthCoreDefaults.RefreshTokenCookieName, refreshA);
        using var repeat =
            await HostClients.PostWithoutBodyAsync(repeatClient, HostClients.LogoutPath);

        // when: отдельно — logout со значением refresh_token='garbage'.
        using var garbageClient = HostClients.Create(_factory);
        HostClients.SetRequestCookie(garbageClient, AuthCoreDefaults.RefreshTokenCookieName, "garbage");
        using var garbage =
            await HostClients.PostWithoutBodyAsync(garbageClient, HostClients.LogoutPath);

        // then: оба — 204 (идемпотентность; несуществующий токен — отсутствие операции).
        Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, garbage.StatusCode);

        // then: refresh-токен другого пользователя продолжает действовать —
        // его /auth/refresh — 204 (чужие токены не отозваны).
        using var refreshForeignClient = HostClients.Create(_factory);
        HostClients.SetRequestCookie(refreshForeignClient, AuthCoreDefaults.RefreshTokenCookieName, refreshB);
        using var refreshForeign =
            await HostClients.PostWithoutBodyAsync(refreshForeignClient, HostClients.RefreshPath);
        Assert.Equal(HttpStatusCode.NoContent, refreshForeign.StatusCode);
    }
}
