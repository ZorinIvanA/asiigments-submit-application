using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-063 (P0, negative; FR-011, FR-022) «/auth/me: без токена и с невалидным
/// токеном — 401».
/// given: Хост запущен; DI-сид-студент — владелец sub подготовленных токенов;
///        access-токен, подписанный другим ключом (ручная сборка HS256,
///        B11AuthSessions.CraftAccessToken), и токен с истёкшим exp.
/// when:  GET /api/v1/auth/me без cookie; с поддельным токеном; с просроченным.
/// then:  Все три — 401 «Не авторизован» (FR-011 AC «Без токена»; FR-022 порядок
///        401): невалидный/просроченный access делает запрос анонимным, защищённый
///        эндпойнт отвечает Challenge с детерминированным телом (ADR-003).
/// </summary>
public sealed class Ts063_MeNoForgedExpiredToken401Tests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    /// <summary>Чужой ключ подписи (≠ Auth__JwtKey стенда) — шаг given «другим ключом».</summary>
    private const string ForeignSigningKey =
        "foreign-signing-key-not-equal-to-b11-stand-0123456789abcdef-0123456789abcdef";

    /// <summary>Сдвиг exp просроченного токена в прошлое (given: «истёкший exp»).</summary>
    private static readonly TimeSpan ExpiredFor = TimeSpan.FromMinutes(15);

    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS063_GetMe_WithoutCookie_ForeignKey_AndExpired_Returns401()
    {
        // given: DI-сид-студент — владелец sub; токены с чужим ключом и истёкшие.
        var owner = B11AuthSessions.SeedStudent(_factory, "b11ts063.owner", "Владелец Токенов");
        var forgedToken = B11AuthSessions.CraftAccessToken(
            owner.Id,
            UserRoles.Student,
            issuedAt: DateTimeOffset.UtcNow,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(15),
            signingKey: ForeignSigningKey);
        var expiredToken = B11AuthSessions.CraftAccessToken(
            owner.Id,
            UserRoles.Student,
            issuedAt: DateTimeOffset.UtcNow - ExpiredFor - TimeSpan.FromMinutes(1),
            expiresAt: DateTimeOffset.UtcNow - ExpiredFor,
            signingKey: B11WebAppFactory.TestJwtKey);

        using var anonymous = HostClients.Create(_factory);
        using var forged = HostClients.Create(_factory);
        forged.DefaultRequestHeaders.Add(
            "Cookie", $"{AuthCoreDefaults.AccessTokenCookieName}={forgedToken}");
        using var expired = HostClients.Create(_factory);
        expired.DefaultRequestHeaders.Add(
            "Cookie", $"{AuthCoreDefaults.AccessTokenCookieName}={expiredToken}");

        // when: GET /api/v1/auth/me без cookie; с поддельным токеном; с просроченным.
        using var withoutCookie = await anonymous.GetAsync("/api/v1/auth/me");
        using var withForgedToken = await forged.GetAsync("/api/v1/auth/me");
        using var withExpiredToken = await expired.GetAsync("/api/v1/auth/me");

        // then: все три — 401 «Не авторизован».
        await ApiAssert.AssertMessageAsync(
            withoutCookie, HttpStatusCode.Unauthorized, "Не авторизован");
        await ApiAssert.AssertMessageAsync(
            withForgedToken, HttpStatusCode.Unauthorized, "Не авторизован");
        await ApiAssert.AssertMessageAsync(
            withExpiredToken, HttpStatusCode.Unauthorized, "Не авторизован");
    }
}
