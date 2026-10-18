using System.Text.Json;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Auth.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Auth.Scenarios;

/// <summary>
/// TS-049 «Access-токен: клеймы sub/role и TTL 15 минут» (happy_path, FR-008,
/// P1).
///
/// given: выполнен успешный вход учителя.
/// when:  декодирование payload access-токена без верификации подписи.
/// then:  sub=uuid пользователя; role='teacher'; exp−iat=900 (FR-008 AC
///        «Access-клеймы»).
/// </summary>
public sealed class Ts049_AccessTokenClaimsTests
{
    private const string Login = "claimsteacher";
    private const string Email = "claimsteacher@example.com";

    [Fact]
    public async Task TeacherLogin_AccessTokenPayload_HasSubRoleAnd900SecondTtl()
    {
        using var factory = new B08AuthDevFactory();
        using var client = B08AuthHost.CreateClient(factory);

        // given: учитель существует (DI-сид — роль teacher) и вошёл успешно.
        var teacher = B08AuthHost.SeedTeacher(factory, Login, Email, "Клеймы Учитель");
        using var login = await client.PostAsync(
            B08AuthHost.LoginEndpoint,
            "{\"login\":\"" + Login + "\",\"password\":\"" + B08AuthHost.TestUserPassword + "\"}");
        B08AuthHost.AssertStatus(login, HttpStatusCode.OK, "успешный вход учителя (TS-049)");

        // when: декодирование payload access-токена без верификации подписи.
        var access = B08AuthHost.SingleCookie(login, "access_token", "Set-Cookie входа учителя (TS-049)");
        var payload = B08AuthHost.DecodeJwtPayload(access.Value);

        // then: sub=uuid пользователя.
        Assert.Equal(JsonValueKind.String, payload.GetProperty("sub").ValueKind);
        Assert.Equal(teacher.Id.ToString("D"), payload.GetProperty("sub").GetString());

        // then: role='teacher'.
        Assert.Equal("teacher", payload.GetProperty("role").GetString());

        // then: exp−iat=900 (Auth__AccessTtlMinutes=15 по умолчанию).
        var iat = payload.GetProperty("iat").GetInt64();
        var exp = payload.GetProperty("exp").GetInt64();
        Assert.Equal(900, exp - iat);
    }
}
