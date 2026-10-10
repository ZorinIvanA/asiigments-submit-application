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
/// TS-070 (P1, negative; FR-011) «/auth/me: невалидный и просроченный access — 401».
/// given: Две cookie: access_token=&lt;мусорная строка&gt;; отдельно access,
///        выпущенный в T0, при часах, переведённых за exp.
/// when:  GET /auth/me с каждой из cookie по отдельности.
/// then:  Оба запроса — 401 «Не авторизован» (FR-011: «с невалидным или
///        просроченным access → 401»).
/// Бизнес-время — FakeTimeProvider хоста (ADR-002): access минтится до перевода
/// часов, затем время уходит за exp (AccessTtlMinutes + 1 минута). Смежные плитки
/// прежних волн: Ts063_AuthMeInvalidTokensTests (токен чужим ключом подписи +
/// просроченный) и Ts066_MeExpiredAccessUnauthorizedTests (просроченный); этот
/// файл замыкает стимул «мусорная строка» дословно по given/when/then кейса.
/// </summary>
public sealed class Ts070_MeGarbageAndExpiredAccessTests(B12FakeTimeWebAppFactory factory)
    : IClassFixture<B12FakeTimeWebAppFactory>
{
    /// <summary>Мусорное значение cookie — не JWT вовсе (given кейса).</summary>
    private const string GarbageAccessToken = "b12-ts070-not-a-jwt-garbage-value";

    private readonly B12FakeTimeWebAppFactory _factory = factory;

    [Fact]
    public async Task Me_WithGarbageString_AndWithAccessExpiredAfterT0_IsUnauthorized()
    {
        // given: access, выпущенный в T0 (DI-минт для сид-преподавателя).
        var teacher = _factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException("Сид-преподаватель не найден — given неисполним.");
        var accessMintedAtT0 = _factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(teacher.Id, UserRoles.Teacher);

        // given: часы переведены за exp (AccessTtlMinutes + 1 минута); TTL refresh —
        // сутки, поэтому на me это не влияет.
        var accessTtlMinutes = _factory.Services.GetRequiredService<IOptions<AuthOptions>>()
            .Value.AccessTtlMinutes;
        _factory.Time.Advance(TimeSpan.FromMinutes(accessTtlMinutes + 1));

        // given: вторая cookie — access_token=<мусорная строка>.
        using var garbageClient = B12AuthSessions.CreateClient(_factory);
        garbageClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={GarbageAccessToken}");

        using var expiredClient = B12AuthSessions.CreateClient(_factory);
        expiredClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={accessMintedAtT0}");

        // when: GET /auth/me с каждой из cookie по отдельности.
        using var withGarbage = await garbageClient.GetAsync(B12AuthEndpoints.Me);
        using var withExpired = await expiredClient.GetAsync(B12AuthEndpoints.Me);

        // then: оба запроса — 401 «Не авторизован».
        await ApiAssert.AssertMessageAsync(
            withGarbage, HttpStatusCode.Unauthorized, ErrorTexts.Unauthorized);
        await ApiAssert.AssertMessageAsync(
            withExpired, HttpStatusCode.Unauthorized, ErrorTexts.Unauthorized);
    }
}
