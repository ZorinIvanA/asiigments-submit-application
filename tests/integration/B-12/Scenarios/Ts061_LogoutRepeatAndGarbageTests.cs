using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-061 (P1 — поднят с P2 ревизией кейса по ISS-002, idempotency; FR-010)
/// «Logout: повтор и мусорное значение cookie не отзывают чужие токены».
/// given: Logout уже выполнен (первый POST с refresh-cookie → 204); существует
///        валидная сессия ДРУГОГО пользователя (DI-минт, ADR-015/ADR-022).
/// when:  Повторный POST /auth/logout с тем же (уже отозванным) refresh-cookie;
///        отдельно logout со значением refresh_token='garbage'.
/// then:  Оба — 204; refresh-токен другого пользователя продолжает действовать —
///        его /auth/refresh — 204 (отзыв чужих/несуществующих токенов не
///        происходит, FR-010).
/// Повторlogout-часть кейса (обе 204, отзыв ровно один раз) дополнительно
/// покрыта плиткой Ts061_LogoutTwiceIdempotentTests, чужие-токены —
/// Ts062_LogoutKeepsForeignTokensTests; этот файл замыкает стимул
/// «мусорное значение cookie» дословно по given/when/then кейса.
/// </summary>
public sealed class Ts061_LogoutRepeatAndGarbageTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task RepeatedLogout_AndGarbageCookie_KeepForeignRefreshAlive()
    {
        // given: пользователь, чей logout будет повторяться, и ДРУГОЙ пользователь
        // с валидной живой refresh-сессией (контрольное значение).
        var exiting = B12Seed.EnsureStudent(
            _factory,
            login: "b12-ts061-exiting",
            fullName: "Повтор Выхода Тестович",
            email: "b12-ts061-exiting@t.local");
        var foreign = B12Seed.EnsureStudent(
            _factory,
            login: "b12-ts061-foreign",
            fullName: "Чужой Токен Тестович",
            email: "b12-ts061-foreign@t.local");
        var exitingRefresh = B12AuthSessions.CreateLiveRefreshToken(_factory, exiting.Id);
        var foreignRefresh = B12AuthSessions.CreateLiveRefreshToken(_factory, foreign.Id);

        using var client = B12AuthSessions.CreateClient(_factory);

        // given: logout уже выполнен — первый выход с refresh-cookie → 204.
        using var firstLogout = await client.SendAsync(
            B12AuthSessions.CreateLogoutRequest(refreshToken: exitingRefresh));
        Assert.Equal(HttpStatusCode.NoContent, firstLogout.StatusCode);

        // when: повторный POST /auth/logout с тем же (уже отозванным) cookie.
        using var repeatedLogout = await client.SendAsync(
            B12AuthSessions.CreateLogoutRequest(refreshToken: exitingRefresh));

        // when: отдельно logout со значением refresh_token='garbage'.
        using var garbageLogout = await client.SendAsync(
            B12AuthSessions.CreateLogoutRequest(refreshToken: "garbage"));

        // then: оба — 204.
        Assert.Equal(HttpStatusCode.NoContent, repeatedLogout.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, garbageLogout.StatusCode);

        // then: refresh-токен другого пользователя продолжает действовать —
        // запись жива в хранилище и его /auth/refresh — 204 (чужие/несуществующие
        // токены не отозваны, FR-010).
        Assert.Null(B12AuthSessions.RequireStoredToken(_factory, foreignRefresh).RevokedAt);
        using var foreignRefreshResponse = await client.SendAsync(
            B12AuthSessions.CreateRefreshRequest(foreignRefresh));
        Assert.Equal(HttpStatusCode.NoContent, foreignRefreshResponse.StatusCode);
    }
}
