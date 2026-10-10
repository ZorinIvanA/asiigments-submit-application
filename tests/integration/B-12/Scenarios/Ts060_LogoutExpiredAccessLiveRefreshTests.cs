using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-060 (P0, boundary; FR-010) «Logout при истёкшем access и живом refresh».
/// given: Access-токен истёк (часы переведены за exp); refresh валиден.
/// when:  POST /auth/logout c refresh-cookie.
/// then:  204 и refresh отозван — последующий /auth/refresh с ним 401 (logout
///        не требует валидного access; FR-010 AC «Истёкший access + живой refresh»).
/// Бизнес-время — FakeTimeProvider хоста (ADR-002): access минтится до перевода
/// часов, затем время уходит за exp (AccessTtlMinutes + 1 минута); refresh
/// (TTL 7 суток) остаётся живым.
/// </summary>
public sealed class Ts060_LogoutExpiredAccessLiveRefreshTests(B12FakeTimeWebAppFactory factory)
    : IClassFixture<B12FakeTimeWebAppFactory>
{
    private readonly B12FakeTimeWebAppFactory _factory = factory;

    [Fact]
    public async Task LogoutWithExpiredAccess_NoContent_RevokesLiveRefresh()
    {
        // given: пользователь вошёл (access + живой refresh).
        var teacher = _factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException("Сид-преподаватель не найден — given неисполним.");
        var accessToken = _factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(teacher.Id, UserRoles.Teacher);
        var refreshToken = B12AuthSessions.CreateLiveRefreshToken(_factory, teacher.Id);

        // given: часы переведены за exp access (access TTL + 1 минута; TTL refresh —
        // сутки, поэтому refresh остаётся живым).
        var accessTtlMinutes = _factory.Services.GetRequiredService<IOptions<AuthOptions>>()
            .Value.AccessTtlMinutes;
        _factory.Time.Advance(TimeSpan.FromMinutes(accessTtlMinutes + 1));

        using var client = B12AuthSessions.CreateClient(_factory);

        // when: POST /auth/logout c refresh-cookie (access просрочен).
        using var request = B12AuthSessions.CreateLogoutRequest(accessToken, refreshToken);
        using var response = await client.SendAsync(request);

        // then: 204; refresh отозван в хранилище (запись с RevokedAt — шов
        // независимой от живости инспекции: FindLiveByHash отозванную запись не отдаёт).
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var stored = B12AuthSessions.RequireStoredTokenIncludingRevoked(_factory, refreshToken);
        Assert.NotNull(stored.RevokedAt);

        // then: последующий /auth/refresh с отозванным токеном — 401.
        using var refreshRequest = B12AuthSessions.CreateRefreshRequest(refreshToken);
        using var refreshResponse = await client.SendAsync(refreshRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }
}
