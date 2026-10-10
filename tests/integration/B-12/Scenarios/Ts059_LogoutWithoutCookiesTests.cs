using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-059 (P0, idempotency; FR-010) «Logout без cookie — идемпотентный 204».
/// given: Запрос не содержит cookie access_token и refresh_token.
/// when:  POST /auth/logout без cookie.
/// then:  204 без ошибок; чужие/несуществующие токены не отозваны (FR-010 AC
///        «Выход без cookie»). Живой refresh-токен другого пользователя после
///        logout остаётся активным в хранилище и продолжает работать (204).
/// </summary>
public sealed class Ts059_LogoutWithoutCookiesTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task LogoutWithoutCookies_NoContent_ForeignTokenStaysActive()
    {
        // given: чужой живой refresh-токен (пользователя B — сид-преподаватель);
        // запрос без cookie access_token и refresh_token.
        var userB = _factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException("Сид-преподаватель не найден — given неисполним.");
        var refreshTokenB = B12AuthSessions.CreateLiveRefreshToken(_factory, userB.Id);
        var storedHashB = B12AuthSessions.RequireStoredToken(_factory, refreshTokenB).TokenHash;

        using var client = B12AuthSessions.CreateClient(_factory);

        // when: POST /auth/logout без cookie.
        using var response = await client.PostAsync(B12AuthEndpoints.Logout, content: null);

        // then: 204 без ошибок.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // then: чужие/несуществующие токены не отозваны — запись B активна,
        // и refresh с cookie B по-прежнему обслуживается (204).
        var storedB = B12AuthSessions.RequireStoredToken(_factory, refreshTokenB);
        Assert.Equal(storedHashB, storedB.TokenHash);
        Assert.Null(storedB.RevokedAt);

        using var refreshRequest = B12AuthSessions.CreateRefreshRequest(refreshTokenB);
        using var refreshResponse = await client.SendAsync(refreshRequest);
        Assert.Equal(HttpStatusCode.NoContent, refreshResponse.StatusCode);
    }
}
