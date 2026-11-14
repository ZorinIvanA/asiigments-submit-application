using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-094 «Смена пароля: успех сохраняет текущую сессию (арбитраж ISS-002)»
/// (happy_path, FR-016 + ISS-002, P0; нумерация текущего батча B-16).
///
/// given: пользователь (пароль 'OldPass1!') авторизован; refresh-cookie текущей
///        сессии присутствует в запросе (DI-сид refresh-токена текущего
///        «устройства», значение — в cookie запроса); у него есть второй
///        refresh-токен (другое устройство); счётчик обнулён — ДЕЛЬТАМИ снимков
///        IKdfCounter.Snapshot() вокруг измеряемого PUT (B16KdfProbe).
/// when:  PUT /me/password {currentPassword:'OldPass1!', password:'NewPass1!',
///        confirmPassword:'NewPass1!'}; затем входы старым/новым паролем,
///        POST /auth/refresh с текущей cookie и со второго устройства.
/// then:  204; вход по новому паролю — 200, по старому — 401; refresh текущей
///        cookie — 204 (сохранён); refresh второго устройства — 401 (отозван);
///        Δkdf=2 (verify+hash) (AC FR-016 «Успешная смена сохраняет текущую
///        сессию», арбитраж ISS-002; IF-013: отзываются ВСЕ, КРОМЕ токена
///        текущей cookie).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts094_PasswordChangeKeepsCurrentSessionTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "ts094pwd";
    private const string Email = "ts094pwd@x.ru";
    private const string OldPassword = "OldPass1!";
    private const string NewPassword = "NewPass1!";

    private readonly B16WebAppFactory _factory;

    public Ts094_PasswordChangeKeepsCurrentSessionTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_Success_KeepsCurrentRefreshCookie_RevokesOtherDevice_AndCountsTwoKdf()
    {
        // given: пользователь (пароль 'OldPass1!'); refresh-токены текущей сессии
        // и второго «устройства».
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-094",
            role: UserRoles.Student,
            groupId: null,
            password: OldPassword);
        var currentDeviceRefresh = B16Harness.SeedRefreshToken(_factory, user.Id);
        var otherDeviceRefresh = B16Harness.SeedRefreshToken(_factory, user.Id);

        // given: сессия, в запросе которой присутствует refresh-cookie текущей
        // сессии (access- и refresh-cookie).
        using var client = B16Harness.CreateSessionClient(
            _factory, user.Id, UserRoles.Student, refreshTokenValue: currentDeviceRefresh);

        // given: «счётчик обнулён» — база Δkdf ровно вокруг PUT.
        var before = B16KdfProbe.Snapshot(_factory.Services);

        // when: PUT {currentPassword:'OldPass1!', password:'NewPass1!',
        // confirmPassword:'NewPass1!'}.
        using var response = await client.PutAsJsonAsync(B16Harness.PasswordEndpoint, new
        {
            currentPassword = OldPassword,
            password = NewPassword,
            confirmPassword = NewPassword,
        });

        // then: 204; Δkdf=2 (verify+hash).
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
        using var newPasswordLogin = await B16Harness.LoginAsync(newLoginClient, Login, NewPassword);
        Assert.True(
            newPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 входа по новому паролю, фактически {(int)newPasswordLogin.StatusCode}: {await newPasswordLogin.Content.ReadAsStringAsync()}");

        // then: вход по старому паролю — 401.
        using var oldLoginClient = B16Harness.Create(_factory);
        using var oldPasswordLogin = await B16Harness.LoginAsync(oldLoginClient, Login, OldPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 входа по старому паролю, фактически {(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");

        // then: refresh с текущей cookie — 204 (токен текущей сессии сохранён,
        // арбитраж ISS-002).
        using var currentRefreshClient = B16Harness.Create(_factory);
        using var currentRefresh = await B16Harness.RefreshAsync(currentRefreshClient, currentDeviceRefresh);
        Assert.True(
            currentRefresh.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204 refresh текущей cookie (сессия сохранена), фактически {(int)currentRefresh.StatusCode}: {await currentRefresh.Content.ReadAsStringAsync()}");

        // then: refresh со второго устройства — 401 (токен отозван).
        using var otherRefreshClient = B16Harness.Create(_factory);
        using var otherRefresh = await B16Harness.RefreshAsync(otherRefreshClient, otherDeviceRefresh);
        Assert.True(
            otherRefresh.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 refresh второго устройства (токен отозван), фактически {(int)otherRefresh.StatusCode}: {await otherRefresh.Content.ReadAsStringAsync()}");
    }
}
