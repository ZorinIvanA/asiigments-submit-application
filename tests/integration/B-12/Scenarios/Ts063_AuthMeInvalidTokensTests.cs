using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-063 (P0, negative; FR-011, FR-022) «/auth/me: без токена и с невалидным
/// токеном — 401».
/// given: Хост запущен; подготовлен access-токен, подписанный другим ключом
///        (ручная сборка HS256, ключ ≠ Auth__JwtKey стенда), и токен с истёкшим
///        exp (минт ITokenService + перевод часов FakeTimeProvider за exp,
///        ADR-002); владелец sub обоих токенов — DI-сид-студент.
/// when:  GET /api/v1/auth/me без cookie; с поддельным токеном; с просроченным.
/// then:  Все три — 401 'Не авторизован' (FR-011 AC «Без токена»; FR-022 порядок
///        401): невалидный/просроченный access делает запрос анонимным, защищённый
///        эндпойнт отвечает Challenge с детерминированным телом (ADR-003).
/// </summary>
public sealed class Ts063_AuthMeInvalidTokensTests(B12FakeTimeWebAppFactory factory)
    : IClassFixture<B12FakeTimeWebAppFactory>
{
    /// <summary>Чужой ключ подписи (≠ Auth__JwtKey стенда) — шаг given «другим ключом».</summary>
    private const string ForeignSigningKey =
        "foreign-signing-key-not-equal-to-b12-stand-0123456789abcdef-0123456789abcdef";

    private readonly B12FakeTimeWebAppFactory _factory = factory;

    [Fact]
    public async Task Me_WithoutCookie_ForeignKeyToken_AndExpiredToken_Returns401()
    {
        // given: сид-преподаватель — владелец sub в подготовленных токенах
        // (DI-сид; владельцу достаточно существующей учётной записи).
        var owner = _factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException("Сид-преподаватель не найден — given неисполним.");

        // given: access-токен, подписанный ДРУГИМ ключом (формат — sub/role/iat).
        var foreignKeyToken = CraftAccessToken(
            owner.Id,
            UserRoles.Student,
            issuedAt: DateTimeOffset.UtcNow,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(15),
            signingKey: ForeignSigningKey);

        // given: токен с истёкшим exp — минт валидного access (ключ стенда) и
        // перевод бизнес-времени хоста за exp (AccessTtlMinutes + 1 минута).
        var expiredToken = _factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(owner.Id, UserRoles.Student);
        var accessTtlMinutes = _factory.Services.GetRequiredService<IOptions<AuthOptions>>()
            .Value.AccessTtlMinutes;
        _factory.Time.Advance(TimeSpan.FromMinutes(accessTtlMinutes + 1));

        using var anonymous = B12AuthSessions.CreateClient(_factory);
        using var foreignKey = B12AuthSessions.CreateClient(_factory);
        foreignKey.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={foreignKeyToken}");
        using var expired = B12AuthSessions.CreateClient(_factory);
        expired.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={expiredToken}");

        // when: GET /auth/me без cookie; с поддельным токеном; с просроченным.
        using var withoutCookie = await anonymous.GetAsync(B12AuthEndpoints.Me);
        using var withForeignKeyToken = await foreignKey.GetAsync(B12AuthEndpoints.Me);
        using var withExpiredToken = await expired.GetAsync(B12AuthEndpoints.Me);

        // then: все три — 401 'Не авторизован'.
        await ApiAssert.AssertMessageAsync(
            withoutCookie, HttpStatusCode.Unauthorized, ErrorTexts.Unauthorized);
        await ApiAssert.AssertMessageAsync(
            withForeignKeyToken, HttpStatusCode.Unauthorized, ErrorTexts.Unauthorized);
        await ApiAssert.AssertMessageAsync(
            withExpiredToken, HttpStatusCode.Unauthorized, ErrorTexts.Unauthorized);
    }

    /// <summary>
    /// Ручная сборка access-JWT (HS256) с заданным ключом подписи — given
    /// «токен, подписанный другим ключом»: sub/role/iat, клеймы дословно.
    /// </summary>
    private static string CraftAccessToken(
        Guid userId,
        string role,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string signingKey)
    {
        var jwt = new JwtSecurityToken(
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D", CultureInfo.InvariantCulture)),
                new Claim(AuthCoreDefaults.RoleClaimType, role),
                new Claim(
                    JwtRegisteredClaimNames.Iat,
                    issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64),
            ],
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
