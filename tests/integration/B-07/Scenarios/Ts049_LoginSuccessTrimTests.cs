using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-049 «Успешный вход: трим логина, 200 + MeDto + cookie» (happy_path,
/// FR-013, P0).
///
/// given: пользователь teacher с паролем 'Passw0rd!' существует (сид фикстуры);
///        предыдущих неуспешных попыток нет.
/// when:  POST /api/v1/auth/login {login:' teacher ', password:'Passw0rd!'}.
/// then:  200; тело MeDto; Set-Cookie access_token и refresh_token. FR-013 AC
///        «Успех» (login ci после трима).
/// </summary>
public sealed class Ts049_LoginSuccessTrimTests : IClassFixture<B07AuthWebAppFactory>
{
    private readonly B07AuthWebAppFactory _factory;

    public Ts049_LoginSuccessTrimTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task LoginWithPaddedLogin_ReturnsMeDtoAndBothCookies()
    {
        // given: пользователь teacher с паролем 'Passw0rd!' существует; неуспешных
        // попыток нет (свежая фикстура класса).
        using var client = B07AuthClients.CreateClient(_factory);

        // when: POST /auth/login {login:' teacher ', password:'Passw0rd!'}.
        using var response = await B07AuthClients.PostLoginAsync(
            client, " teacher ", B07AuthWebAppFactory.TeacherPassword);

        // then: 200; тело MeDto учётной записи teacher (login ci после трима).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.StringPropertyIs(body, "login", "teacher");
        BodyAssertions.StringPropertyIs(body, "role", UserRoles.Teacher);

        // then: Set-Cookie access_token и refresh_token (IF-004).
        var access = B07AuthClients.RequiredSetCookie(response, AuthCoreDefaults.AccessTokenCookieName);
        var refresh = B07AuthClients.RequiredSetCookie(response, AuthCoreDefaults.RefreshTokenCookieName);
        Assert.True(access.Length > 0, "Set-Cookie access_token без значения.");
        Assert.True(refresh.Length > 0, "Set-Cookie refresh_token без значения.");
    }
}
