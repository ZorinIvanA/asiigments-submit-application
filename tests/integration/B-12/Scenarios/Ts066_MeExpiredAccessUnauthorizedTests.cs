using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-066 (P1, boundary; FR-011) «auth/me с просроченным access — 401».
/// given: Access-токен выпущен ранее; инжектируемые часы переведены за exp.
/// when:  GET /auth/me с этим access-cookie.
/// then:  401 «Не авторизован» (FR-011: «с невалидным или просроченным access → 401»).
/// Бизнес-время — FakeTimeProvider хоста (ADR-002): access минтится до перевода
/// часов, затем время уходит за exp (AccessTtlMinutes + 1 минута).
/// </summary>
public sealed class Ts066_MeExpiredAccessUnauthorizedTests(B12FakeTimeWebAppFactory factory)
    : IClassFixture<B12FakeTimeWebAppFactory>
{
    private readonly B12FakeTimeWebAppFactory _factory = factory;

    [Fact]
    public async Task Me_AfterAccessExpired_IsUnauthorized()
    {
        // given: access-токен выпущен (для сид-преподавателя).
        var teacher = _factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException("Сид-преподаватель не найден — given неисполним.");
        using var client = B12AuthSessions.CreateClientWithAccess(
            _factory, teacher.Id, UserRoles.Teacher);

        // given: инжектируемые часы переведены за exp access (TTL + 1 минута).
        var accessTtlMinutes = _factory.Services.GetRequiredService<IOptions<AuthOptions>>()
            .Value.AccessTtlMinutes;
        _factory.Time.Advance(TimeSpan.FromMinutes(accessTtlMinutes + 1));

        // when: GET /auth/me с просроченным access-cookie.
        using var response = await client.GetAsync(B12AuthEndpoints.Me);

        // then: 401 «Не авторизован».
        await ApiAssert.AssertMessageAsync(response, HttpStatusCode.Unauthorized, ErrorTexts.Unauthorized);
    }
}
