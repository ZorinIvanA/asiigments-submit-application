using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-086 «me/password: смена без refresh-cookie отзывает все refresh-токены»
/// (negative, FR-016, P1).
///
/// given: пользователь авторизован только access-cookie (заголовок Cookie без
///        refresh_token); в хранилище есть его refresh-токены — два устройства,
///        наминчены через ITokenService + ISecurityTokenRepository.
/// when:  PUT me/password с верным currentPassword (refresh-cookie не передаётся).
/// then:  204; все refresh-токены пользователя отозваны — оба устройства
///        получают 401 на /auth/refresh (FR-016 AC «Смена без refresh-cookie
///        отзывает все», IF-013: «cookie нет — отзываются все»).
/// </summary>
public sealed class Ts086_MePasswordNoRefreshCookieRevokesAllTests : IClassFixture<B15PasswordWebAppFactory>
{
    private const string Login = "ts086user";
    private const string Email = "ts086@example.com";
    private const string FullName = "Пользователь ВосемьдесятШесть";
    private const string OldPassword = "OldPass1!";
    private const string NewPassword = "NewPass1!";

    private readonly B15PasswordWebAppFactory _factory;

    public Ts086_MePasswordNoRefreshCookieRevokesAllTests(B15PasswordWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithoutRefreshCookie_RevokesEveryRefreshToken()
    {
        // given: сессия только с access-cookie; в хранилище два refresh-токена.
        var user = B15PasswordSessions.SeedUserWithPassword(
            _factory, Login, Email, FullName, OldPassword);
        var firstDeviceRefresh = B15PasswordSessions.MintRefreshToken(_factory, user.Id);
        var secondDeviceRefresh = B15PasswordSessions.MintRefreshToken(_factory, user.Id);
        using var client = B15PasswordSessions.CreateSessionClient(_factory, user.Id);

        // when: смена пароля без refresh-cookie (верный currentPassword).
        using var response = await client.PutAsJsonAsync(B15PasswordSessions.MePasswordEndpoint, new
        {
            currentPassword = OldPassword,
            password = NewPassword,
            confirmPassword = NewPassword,
        });

        // then: 204; оба refresh-токена отозваны — 401 на /auth/refresh.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using (var firstClient = B15PasswordSessions.CreateRefreshOnlyClient(_factory, firstDeviceRefresh))
        using (var refreshFirst = await firstClient.PostAsync(
                   B15PasswordSessions.RefreshEndpoint, content: null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, refreshFirst.StatusCode);
        }

        using (var secondClient = B15PasswordSessions.CreateRefreshOnlyClient(_factory, secondDeviceRefresh))
        using (var refreshSecond = await secondClient.PostAsync(
                   B15PasswordSessions.RefreshEndpoint, content: null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, refreshSecond.StatusCode);
        }
    }
}
