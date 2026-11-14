using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-085 «me/password: успех сохраняет текущую сессию, отзывает прочие
/// (ISS-002)» (happy_path, FR-016, P0).
///
/// given: refresh-cookie текущей сессии присутствует (DI-сид refresh-токена
///        «текущего устройства», значение — в cookie запроса); у пользователя
///        есть второй refresh-токен (другое устройство); счётчик KDF «сброшен» —
///        ДЕЛЬТАМИ снимков IKdfCounter.Snapshot() вокруг измеряемого PUT; старый
///        пароль известен (DI-сид с реальным PBKDF2-хэшем).
/// when:  PUT {currentPassword: верный, password:'NewPass1!',
///        confirmPassword:'NewPass1!'}; затем вход по новому и старому паролю;
///        POST /auth/refresh обоими refresh-токенами.
/// then:  204; вход по новому паролю — 200, по старому — 401; refresh текущей
///        cookie — 204 (сохранён); refresh второго устройства — 401 (отозван);
///        Δkdf=2 (verify+hash) (FR-016 AC «Успешная смена сохраняет текущую
///        сессию», арбитраж ISS-002; IF-013: отзываются ВСЕ refresh-токены,
///        КРОМЕ совпадающего с refresh-cookie текущего запроса).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts085_PasswordChangeKeepsCurrentSessionTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "ts085";
    private const string Email = "ts085@x.ru";

    private readonly B16WebAppFactory _factory;

    public Ts085_PasswordChangeKeepsCurrentSessionTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_Success_KeepsCurrentRefreshCookie_RevokesOtherDevice_AndCountsTwoKdf()
    {
        // given: пользователь; refresh-токены текущей сессии и второго «устройства».
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-085",
            role: UserRoles.Student,
            groupId: null,
            password: B16Harness.TestUserPassword);
        var currentDeviceRefresh = B16Harness.SeedRefreshToken(_factory, user.Id);
        var otherDeviceRefresh = B16Harness.SeedRefreshToken(_factory, user.Id);

        // given: сессия с access- И refresh-cookie текущего запроса.
        using var client = B16Harness.CreateSessionClient(
            _factory, user.Id, UserRoles.Student, refreshTokenValue: currentDeviceRefresh);

        // given: «счётчик KDF сброшен» — база Δkdf ровно вокруг PUT.
        var before = B16KdfProbe.Snapshot(_factory.Services);

        // when: успешная смена пароля (верный текущий, валидный новый).
        using var response = await client.PutAsJsonAsync(B16Harness.PasswordEndpoint, new
        {
            currentPassword = B16Harness.TestUserPassword,
            password = B16Harness.NewPassword,
            confirmPassword = B16Harness.NewPassword,
        });

        // then: 204; Δkdf=2 (verify текущего + hash нового).
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var after = B16KdfProbe.Snapshot(_factory.Services);
        var delta = B16KdfProbe.TotalDelta(before, after);
        Assert.True(
            delta == 2,
            $"Ожидался Δkdf=2 (verify+hash), фактически {delta}.");

        // then: вход по новому паролю — 200.
        using var newLoginClient = B16Harness.Create(_factory);
        using var newPasswordLogin = await B16Harness.LoginAsync(
            newLoginClient, Login, B16Harness.NewPassword);
        Assert.True(
            newPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 входа по новому паролю, фактически {(int)newPasswordLogin.StatusCode}: {await newPasswordLogin.Content.ReadAsStringAsync()}");

        // then: вход по старому паролю — 401.
        using var oldLoginClient = B16Harness.Create(_factory);
        using var oldPasswordLogin = await B16Harness.LoginAsync(
            oldLoginClient, Login, B16Harness.TestUserPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 входа по старому паролю, фактически {(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");

        // then: refresh текущей cookie — 204 (токен текущей сессии сохранён, ISS-002).
        using var currentRefreshClient = B16Harness.Create(_factory);
        using var currentRefresh = await B16Harness.RefreshAsync(currentRefreshClient, currentDeviceRefresh);
        Assert.True(
            currentRefresh.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204 refresh текущей cookie (сессия сохранена), фактически {(int)currentRefresh.StatusCode}: {await currentRefresh.Content.ReadAsStringAsync()}");

        // then: refresh второго устройства — 401 (токен отозван).
        using var otherRefreshClient = B16Harness.Create(_factory);
        using var otherRefresh = await B16Harness.RefreshAsync(otherRefreshClient, otherDeviceRefresh);
        Assert.True(
            otherRefresh.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 refresh второго устройства (токен отозван), фактически {(int)otherRefresh.StatusCode}: {await otherRefresh.Content.ReadAsStringAsync()}");
    }
}
