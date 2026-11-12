using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LabsApp.Auth;
using LabsApp.IntegrationTests.B06.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-032 «role-claim вне словаря в JWT: 401» (негативный, P0, FR-011;
/// User.role constraints: «role в JWT вне словаря → 401»).
///
/// given: харнес знает тестовый Auth__JwtKey (>=32 симв.); харнесом сформирован JWT
///        HS256 с claims sub=uuid существующего пользователя, role='admin', jti,
///        действительным exp/iat.
/// when:  GET /api/v1/labs с cookie access_token=этот JWT.
/// then:  HTTP 401 «Не авторизован».
/// </summary>
public sealed class Ts032_RoleClaimOutsideDictionaryTests : IClassFixture<B06WebAppFactory>
{
    private readonly B06WebAppFactory _factory;

    public Ts032_RoleClaimOutsideDictionaryTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetLabsWithJwtWhoseRoleIsOutsideDictionary_Returns401Unauthorized()
    {
        // given: uuid существующего пользователя (DI-сид) + JWT харнеса с role='admin'.
        var user = TestSessions.SeedUser(
            _factory,
            login: "ts032.student",
            email: "ts032@example.com");
        var forgedJwt = IssueAdminRoleJwt(user.Id);

        // given: cookie access_token=этот JWT.
        using var client = HostClients.CreateWithCookie(
            _factory,
            AuthCoreDefaults.AccessTokenCookieName,
            forgedJwt);

        // when: GET /api/v1/labs.
        using var response = await client.GetAsync(ApiRequests.LabsEndpoint);

        // then: HTTP 401 «Не авторизован» — роль вне словаря {student, teacher}.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Не авторизован");
    }

    /// <summary>
    /// JWT HS256, сформированный харнесом ключом Auth__JwtKey тестового хоста:
    /// sub/jti/exp/iat валидны, role='admin' — вне словаря. MapInboundClaims=false —
    /// имена claims не транслируются (как в JwtTokenService).
    /// </summary>
    private static string IssueAdminRoleJwt(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var jwt = new JwtSecurityToken(
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D", CultureInfo.InvariantCulture)),
                new Claim(AuthCoreDefaults.RoleClaimType, "admin"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture)),
                new Claim(
                    JwtRegisteredClaimNames.Iat,
                    now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64),
            },
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(15).UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(B06WebAppFactory.TestJwtKey)),
                SecurityAlgorithms.HmacSha256));
        return handler.WriteToken(jwt);
    }
}
