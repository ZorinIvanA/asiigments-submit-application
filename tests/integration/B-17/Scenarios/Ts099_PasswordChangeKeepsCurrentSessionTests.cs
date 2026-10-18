using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-099 «Смена пароля: успех сохраняет текущую сессию (арбитраж ISS-002)»
/// (happy_path, FR-016, P0).
///
/// given: пользователь имеет refresh-cookie текущей сессии И второй
///        refresh-токен (другое устройство — DI-сид обоих refresh-токенов,
///        значение текущего — в cookie запроса); счётчик KDF «обнулён» —
///        дельты снимков IKdfCounter.Snapshot(); известны старый
///        ('student123!') и новый пароли (DI-сид с реальным PBKDF2-хэшем).
/// when:  PUT /me/password {currentPassword:'student123!',
///        password:'NewPass1!', confirmPassword:'NewPass1!'} с refresh-cookie
///        текущей сессии; затем входы старым/новым паролем; /auth/refresh
///        текущей cookie и токена второго устройства.
/// then:  204; вход по новому паролю — 200, по старому — 401; refresh текущей
///        cookie — 204 (всё ещё валиден); refresh второго устройства — 401
///        (отозван); Δkdf=2 (verify+hash) (FR-016 AC «Успешная смена сохраняет
///        текущую сессию», арбитраж ISS-002; IF-013: отзываются ВСЕ
///        refresh-токены, КРОМЕ совпадающего с refresh-cookie текущего запроса).
/// </summary>
public sealed class Ts099_PasswordChangeKeepsCurrentSessionTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts099";
    private const string Email = "ts099@x.ru";

    private readonly B17WebAppFactory _factory;

    public Ts099_PasswordChangeKeepsCurrentSessionTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_Success_KeepsCurrentRefreshCookie_RevokesOtherDevice_AndCountsTwoKdf()
    {
        // given: пользователь; refresh-токены текущей сессии и второго «устройства».
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-099",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        var currentDeviceRefresh = B17ProfileHost.SeedRefreshToken(_factory, user.Id);
        var otherDeviceRefresh = B17ProfileHost.SeedRefreshToken(_factory, user.Id);

        // given: сессия с access- И refresh-cookie текущего запроса.
        using var client = B17ProfileHost.CreateSessionClient(
            _factory, user.Id, UserRoles.Student, refreshTokenValue: currentDeviceRefresh);

        // given: «счётчик KDF обнулён» — база Δkdf ровно вокруг PUT.
        var before = B17ProfileHost.KdfSnapshot(_factory);

        // when: успешная смена пароля (верный текущий, валидный новый).
        using var response = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
        {
            currentPassword = B17ProfileHost.TestUserPassword,
            password = B17ProfileHost.NewPassword,
            confirmPassword = B17ProfileHost.NewPassword,
        });

        // then: 204; Δkdf=2 (verify текущего + hash нового).
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var after = B17ProfileHost.KdfSnapshot(_factory);
        var delta = B17ProfileHost.KdfTotalDelta(before, after);
        Assert.True(
            delta == 2,
            $"Ожидался Δkdf=2 (verify+hash), фактически {delta}.");

        // then: вход по новому паролю — 200.
        using var newLoginClient = B17ProfileHost.Create(_factory);
        using var newPasswordLogin = await B17ProfileHost.LoginAsync(
            newLoginClient, Login, B17ProfileHost.NewPassword);
        Assert.True(
            newPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 входа по новому паролю, фактически {(int)newPasswordLogin.StatusCode}: {await newPasswordLogin.Content.ReadAsStringAsync()}");

        // then: вход по старому паролю — 401.
        using var oldLoginClient = B17ProfileHost.Create(_factory);
        using var oldPasswordLogin = await B17ProfileHost.LoginAsync(
            oldLoginClient, Login, B17ProfileHost.TestUserPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 входа по старому паролю, фактически {(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");

        // then: refresh текущей cookie — 204 (токен текущей сессии сохранён, ISS-002).
        using var currentRefreshClient = B17ProfileHost.Create(_factory);
        using var currentRefresh = await B17ProfileHost.RefreshAsync(currentRefreshClient, currentDeviceRefresh);
        Assert.True(
            currentRefresh.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204 refresh текущей cookie (сессия сохранена), фактически {(int)currentRefresh.StatusCode}: {await currentRefresh.Content.ReadAsStringAsync()}");

        // then: refresh второго устройства — 401 (токен отозван).
        using var otherRefreshClient = B17ProfileHost.Create(_factory);
        using var otherRefresh = await B17ProfileHost.RefreshAsync(otherRefreshClient, otherDeviceRefresh);
        Assert.True(
            otherRefresh.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 refresh второго устройства (токен отозван), фактически {(int)otherRefresh.StatusCode}: {await otherRefresh.Content.ReadAsStringAsync()}");
    }
}
