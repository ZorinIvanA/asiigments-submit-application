using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-057 (P1, concurrency; FR-009) «Refresh: параллельные запросы идемпотентны
/// без ротации».
/// given: Одна валидная refresh-cookie.
/// when:  10 параллельных POST /auth/refresh с этой cookie.
/// then:  Все — 204; исключений нет; в хранилище по-прежнему одна активная запись
///        refresh-токена пользователя (без ротации; FR-009 «Параллельные
///        refresh-запросы допустимы и все получают 204»).
/// </summary>
public sealed class Ts057_RefreshParallelRequestsIdempotentTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TenParallelRefreshes_AllNoContent_SingleActiveStoredRecord()
    {
        // given: одна валидная refresh-cookie (DI-минт, ADR-015: без POST /auth/login).
        var teacher = _factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException("Сид-преподаватель не найден — given неисполним.");
        var refreshTokenValue = B12AuthSessions.CreateLiveRefreshToken(_factory, teacher.Id);
        var storedBefore = B12AuthSessions.RequireStoredToken(_factory, refreshTokenValue);
        var storedHash = storedBefore.TokenHash;
        var expiresAtBefore = storedBefore.ExpiresAt;

        using var client = B12AuthSessions.CreateClient(_factory);

        // when: 10 параллельных POST /auth/refresh с этой cookie (отдельное сообщение
        // на запрос; исход исключения любого запроса уронил бы Task.WhenAll).
        var requests = Enumerable.Range(0, 10)
            .Select(_ => B12AuthSessions.CreateRefreshRequest(refreshTokenValue))
            .ToList();
        var responses = await Task.WhenAll(requests.Select(request => client.SendAsync(request)));

        // then: все — 204; refresh не ротируется (Set-Cookie refresh_token нет).
        Assert.All(responses, response =>
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.False(
                B12AuthSessions.TryGetSetCookie(response, AuthCoreDefaults.RefreshTokenCookieName, out _),
                "refresh не ротируется: Set-Cookie refresh_token при refresh не допускается.");
        });

        // then: в хранилище по-прежнему одна активная запись refresh-токена пользователя:
        // единственная DI-запись жива (не отозвана) и не переиздавалась (expiresAt прежний).
        var storedAfter = B12AuthSessions.RequireStoredToken(_factory, refreshTokenValue);
        Assert.Equal(storedHash, storedAfter.TokenHash);
        Assert.Equal(expiresAtBefore, storedAfter.ExpiresAt);
        Assert.Null(storedAfter.RevokedAt);
    }
}
