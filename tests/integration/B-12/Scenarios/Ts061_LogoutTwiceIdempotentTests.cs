using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-061 (P1, idempotency; FR-010) «Двойной logout — оба 204».
/// given: Пользователь вошёл.
/// when:  POST /auth/logout дважды подряд с одним и тем же refresh-cookie.
/// then:  Оба ответа — 204; исключений нет; запись токена отозвана ровно один раз
///        (идемпотентность FR-010). Момент отзыва (RevokedAt) после второго logout
///        совпадает с моментом после первого — повторный выход запись не переотзывает.
/// </summary>
public sealed class Ts061_LogoutTwiceIdempotentTests(B12FakeTimeWebAppFactory factory)
    : IClassFixture<B12FakeTimeWebAppFactory>
{
    private readonly B12FakeTimeWebAppFactory _factory = factory;

    [Fact]
    public async Task DoubleLogout_BothNoContent_RevokedExactlyOnce()
    {
        // given: пользователь вошёл (живой refresh-cookie).
        var teacher = _factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException("Сид-преподаватель не найден — given неисполним.");
        var refreshToken = B12AuthSessions.CreateLiveRefreshToken(_factory, teacher.Id);

        using var client = B12AuthSessions.CreateClient(_factory);

        // when: первый POST /auth/logout с одним и тем же refresh-cookie.
        using var firstRequest = B12AuthSessions.CreateLogoutRequest(refreshToken: refreshToken);
        using var first = await client.SendAsync(firstRequest);
        var revokedAtAfterFirst = B12AuthSessions
            .RequireStoredTokenIncludingRevoked(_factory, refreshToken).RevokedAt;

        // when: бизнес-время сдвигается — повторный отзыв изменил бы момент отзыва.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        // when: второй POST /auth/logout с тем же refresh-cookie.
        using var secondRequest = B12AuthSessions.CreateLogoutRequest(refreshToken: refreshToken);
        using var second = await client.SendAsync(secondRequest);

        // then: оба ответа — 204.
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        // then: запись отозвана ровно один раз — момент отзыва не изменился
        // (инспекция записи независимо от живости: FindLiveByHash отозванную
        // запись не отдаёт).
        Assert.NotNull(revokedAtAfterFirst);
        var storedAfterSecond = B12AuthSessions.RequireStoredTokenIncludingRevoked(_factory, refreshToken);
        Assert.Equal(revokedAtAfterFirst, storedAfterSecond.RevokedAt);
    }
}
