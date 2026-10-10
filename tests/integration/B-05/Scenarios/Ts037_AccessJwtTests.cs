using System.IdentityModel.Tokens.Jwt;
using System.Text;
using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-037 (P0, nfr; FR-010) «Access-JWT: HS256, подпись ключом конфигурации,
/// состав claims».
///
/// given: харнес с Auth__AccessTtlMinutes=15 и известным Auth__JwtKey; успешный
///        вход teacher (сид-преподаватель, POST /auth/login, 200).
/// when:  разбор cookie access_token.
/// then:  алгоритм HS256; подпись проверяется ключом Auth__JwtKey харнеса;
///        claims: sub=uuid пользователя, role='teacher', jti (uuid),
///        exp−iat=15*60 секунд. FR-010 AC «Access-JWT».
/// </summary>
public sealed class Ts037_AccessJwtTests(B05AccessTtlWebAppFactory factory)
    : IClassFixture<B05AccessTtlWebAppFactory>
{
    /// <summary>Имя claims sub/role/jti/iat/exp — исходные имена (MapInboundClaims=false).</summary>
    private const string SubClaim = "sub";
    private const string RoleClaim = "role";
    private const string JtiClaim = "jti";
    private const string IatClaim = "iat";
    private const string ExpClaim = "exp";

    private readonly B05AccessTtlWebAppFactory _factory = factory;

    [Fact]
    public async Task TS037_AccessJwtCookie_IsHs256SignedByHarnessKey_WithCaseClaims()
    {
        // given: успешный вход teacher (сид-преподаватель харнеса).
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var teacher = users.GetByLogin(SeedOptions.DefaultTeacherLogin);
        Assert.True(
            teacher is not null && teacher.Role == "teacher",
            "Предусловие кейса: сид-преподаватель с ролью teacher обязан существовать в харнесе.");

        using var client = B05SecurityClients.CreateClient(_factory);
        using var login = await B05SecurityClients.PostLoginAsync(
            client, SeedOptions.DefaultTeacherLogin, B05AccessTtlWebAppFactory.TestTeacherPassword);
        Assert.True(
            login.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: вход teacher должен вернуть 200, фактически {login.StatusCode}: " +
            $"{await login.Content.ReadAsStringAsync()}");

        // when: разбор cookie access_token.
        var accessCookie = B05SecurityClients.RequiredSetCookie(login, AuthCoreDefaults.AccessTokenCookieName);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var jwt = handler.ReadJwtToken(accessCookie);

        // then: алгоритм HS256.
        Assert.True(
            string.Equals(jwt.Header.Alg, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal),
            $"Ожидался алгоритм подписи HS256, фактически «{jwt.Header.Alg}».");

        // then: подпись проверяется ключом Auth__JwtKey харнеса (ключ конфигурации —
        //       известный ключ фикстуры; валидация подписи тем же ключом проходит).
        var configuredKey = _factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value.JwtKey;
        Assert.Equal(B05AccessTtlWebAppFactory.TestJwtKey, configuredKey);
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuredKey!)),
            ClockSkew = TimeSpan.Zero,
        };
        var principal = handler.ValidateToken(accessCookie, validationParameters, out _);
        Assert.True(principal is not null, "Токен обязан проходить валидацию подписи ключом харнеса.");

        // then: claims — sub=uuid пользователя, role='teacher', jti (uuid).
        Assert.Equal(teacher!.Id.ToString("D"), principal.FindFirst(SubClaim)?.Value);
        Assert.Equal("teacher", principal.FindFirst(RoleClaim)?.Value);
        Assert.True(
            Guid.TryParse(principal.FindFirst(JtiClaim)?.Value, out var jti) && jti != Guid.Empty,
            $"Claim jti обязан быть uuid, фактически «{principal.FindFirst(JtiClaim)?.Value}».");

        // then: exp−iat = 15*60 секунд (Auth__AccessTtlMinutes харнеса).
        var issuedAt = ReadLongClaim(jwt, IatClaim);
        var expiresAt = ReadLongClaim(jwt, ExpClaim);
        Assert.Equal(15 * 60, expiresAt - issuedAt);
    }

    private static long ReadLongClaim(JwtSecurityToken jwt, string claimType)
    {
        var raw = jwt.Payload.TryGetValue(claimType, out var value) ? value?.ToString() : null;
        Assert.True(
            long.TryParse(raw, out var parsed),
            $"Claim {claimType} обязан быть числом (Unix-секунды), фактически «{raw}».");
        return parsed;
    }
}
