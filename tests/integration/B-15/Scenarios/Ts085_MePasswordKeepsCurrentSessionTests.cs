using LabsApp.IntegrationTests.B15.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-085 «me/password: успех сохраняет текущую сессию, отзывает прочие
/// (ISS-002)» (happy_path, FR-016, P0).
///
/// given: refresh-cookie текущей сессии присутствует (в заголовке Cookie
///        access_token + refresh_token текущего устройства); у пользователя есть
///        второй refresh-токен (другое устройство) — оба минтятся через
///        ITokenService + ISecurityTokenRepository (образец B10AuthSessions);
///        счётчик KDF измеряется дельтами; старый пароль известен ('OldPass1!',
///        DI-сид через IPasswordHasher).
/// when:  PUT {currentPassword:'OldPass1!', password:'NewPass1!',
///        confirmPassword:'NewPass1!'}.
/// then:  204; вход по новому паролю — 200, по старому — 401; refresh текущей
///        cookie всё ещё валиден (последующий /auth/refresh — 204); refresh
///        второго устройства отозван (его /auth/refresh — 401); Δkdf = 2
///        (verify + hash, метка change_password) — FR-016 AC «Успешная смена
///        сохраняет текущую сессию», арбитраж ISS-002.
/// </summary>
public sealed class Ts085_MePasswordKeepsCurrentSessionTests : IClassFixture<B15PasswordWebAppFactory>
{
    private const string Login = "ts085user";
    private const string Email = "ts085@example.com";
    private const string FullName = "Пользователь ВосемьдесятПять";
    private const string OldPassword = "OldPass1!";
    private const string NewPassword = "NewPass1!";

    private readonly B15PasswordWebAppFactory _factory;

    public Ts085_MePasswordKeepsCurrentSessionTests(B15PasswordWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_Success_KeepsCurrentRefresh_RevokesOther_AndCountsTwoDerivations()
    {
        // given: пользователь со старым паролем и ДВУМЯ refresh-токенами
        // (текущее устройство + второе устройство).
        var user = B15PasswordSessions.SeedUserWithPassword(
            _factory, Login, Email, FullName, OldPassword);
        var currentDeviceRefresh = B15PasswordSessions.MintRefreshToken(_factory, user.Id);
        var otherDeviceRefresh = B15PasswordSessions.MintRefreshToken(_factory, user.Id);

        // Сессия текущего устройства: access-cookie + refresh-cookie текущего запроса.
        using var client = B15PasswordSessions.CreateSessionClient(
            _factory, user.Id, refreshToken: currentDeviceRefresh);

        var before = B15KdfProbe.Snapshot(_factory.Services);

        // when: успешная смена пароля с refresh-cookie текущего запроса.
        using var response = await client.PutAsJsonAsync(B15PasswordSessions.MePasswordEndpoint, new
        {
            currentPassword = OldPassword,
            password = NewPassword,
            confirmPassword = NewPassword,
        });

        // then: 204 и Δkdf = 2 (verify + hash, метка change_password).
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = B15KdfProbe.Snapshot(_factory.Services);
        Assert.Equal(2, B15KdfProbe.CallerDelta(before, after, "change_password"));

        // Вход по новому паролю — 200.
        using (var loginClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
               {
                   AllowAutoRedirect = false,
                   HandleCookies = false,
               }))
        {
            using var loginNew = await loginClient.PostAsJsonAsync(
                B15PasswordSessions.LoginEndpoint,
                new { login = Login, password = NewPassword });
            Assert.Equal(HttpStatusCode.OK, loginNew.StatusCode);

            // Вход по старому паролю — 401 (один отказ лимитер входа не блокирует).
            using var loginOld = await loginClient.PostAsJsonAsync(
                B15PasswordSessions.LoginEndpoint,
                new { login = Login, password = OldPassword });
            Assert.Equal(HttpStatusCode.Unauthorized, loginOld.StatusCode);
        }

        // Refresh текущей cookie всё ещё валиден — 204 (арбитраж ISS-002:
        // «все, КРОМЕ токена текущей cookie»).
        using (var currentRefreshClient =
                   B15PasswordSessions.CreateRefreshOnlyClient(_factory, currentDeviceRefresh))
        using (var refreshCurrent = await currentRefreshClient.PostAsync(
                   B15PasswordSessions.RefreshEndpoint, content: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, refreshCurrent.StatusCode);
        }

        // Refresh второго устройства отозван — 401.
        using (var otherRefreshClient =
                   B15PasswordSessions.CreateRefreshOnlyClient(_factory, otherDeviceRefresh))
        using (var refreshOther = await otherRefreshClient.PostAsync(
                   B15PasswordSessions.RefreshEndpoint, content: null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, refreshOther.StatusCode);
        }
    }
}
