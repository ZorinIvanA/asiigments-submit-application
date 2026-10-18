using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-062 (P1, negative; FR-010) «Logout не отзывает чужие токены».
/// given: Два пользователя A и B с активными сессиями.
/// when:  A выполняет POST /auth/logout.
/// then:  204; refresh-токен B остаётся действительным — POST /auth/refresh с
///        cookie B — 204 (FR-010: «Отзыв чужих/несуществующих токенов не происходит»).
/// </summary>
public sealed class Ts062_LogoutKeepsForeignTokensTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task LogoutOfUserA_KeepsRefreshOfUserBLive()
    {
        // given: пользователь A (сид-преподаватель) и пользователь B (DI-сид студента),
        // у обоих живые refresh-токены.
        var userA = _factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException("Сид-преподаватель не найден — given неисполним.");
        var userB = B12Seed.EnsureStudent(
            _factory,
            login: "b12-ts062-student",
            fullName: "Токенов Чужих Тестович",
            email: "b12-ts062@t.local");
        var refreshTokenA = B12AuthSessions.CreateLiveRefreshToken(_factory, userA.Id);
        var refreshTokenB = B12AuthSessions.CreateLiveRefreshToken(_factory, userB.Id);

        using var client = B12AuthSessions.CreateClient(_factory);

        // when: A выполняет POST /auth/logout (с cookies своей сессии).
        using var logoutRequest = B12AuthSessions.CreateLogoutRequest(refreshToken: refreshTokenA);
        using var logoutResponse = await client.SendAsync(logoutRequest);

        // then: 204; запись токена A отозвана (шов инспекции независимо от
        // живости: FindLiveByHash отозванную запись не отдаёт).
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.NotNull(
            B12AuthSessions.RequireStoredTokenIncludingRevoked(_factory, refreshTokenA).RevokedAt);

        // then: refresh-токен B остаётся действительным — запись ЖИВА по
        // контракту хранилища и POST /auth/refresh с cookie B — 204.
        Assert.Null(B12AuthSessions.RequireStoredToken(_factory, refreshTokenB).RevokedAt);
        using var refreshRequest = B12AuthSessions.CreateRefreshRequest(refreshTokenB);
        using var refreshResponse = await client.SendAsync(refreshRequest);
        Assert.Equal(HttpStatusCode.NoContent, refreshResponse.StatusCode);
    }
}
