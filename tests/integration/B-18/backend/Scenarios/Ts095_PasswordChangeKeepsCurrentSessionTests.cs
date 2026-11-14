using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-095 «Смена пароля: успех сохраняет текущую сессию (арбитраж ISS-002)»
/// (happy_path, FR-016, P0).
///
/// given: refresh-cookie текущей сессии присутствует (DI-сид refresh-токена
///        «текущего устройства», значение — в cookie запроса); у пользователя есть
///        второй refresh-токен (второй вход, другое «устройство»); счётчик KDF
///        обнулён — ДЕЛЬТАМИ снимков IKdfCounter.Snapshot() вокруг измеряемого PUT.
/// when:  PUT /me/password с верным currentPassword и валидным новым; затем вход по
///        новому и старому паролю; POST /auth/refresh обоими refresh-токенами.
/// then:  204; вход по новому паролю — 200, по старому — 401; refresh текущей
///        cookie — 204 (сохранён); refresh второго устройства — 401 (отозван);
///        Δkdf=2 (verify+hash) (FR-016 AC «Успешная смена сохраняет текущую
///        сессию»; IF-013: отзываются ВСЕ refresh-токены, КРОМЕ совпадающего с
///        refresh-cookie текущего запроса).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts095_PasswordChangeKeepsCurrentSessionTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "ts095";
    private const string Email = "ts095@x.ru";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts095_PasswordChangeKeepsCurrentSessionTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_Success_KeepsCurrentRefreshCookie_RevokesOtherDevice_AndCountsTwoKdf()
    {
        // given: пользователь; refresh-токены текущей сессии и второго «устройства».
        var user = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-095",
            role: UserRoles.Student,
            groupId: null,
            password: B18ProfileHarness.TestUserPassword);
        var currentDeviceRefresh = B18ProfileHarness.SeedRefreshToken(_factory, user.Id);
        var otherDeviceRefresh = B18ProfileHarness.SeedRefreshToken(_factory, user.Id);

        // given: сессия с access- И refresh-cookie текущего запроса.
        using var client = B18ProfileHarness.CreateSessionClient(
            _factory, user.Id, UserRoles.Student, refreshTokenValue: currentDeviceRefresh);

        // given: «счётчик KDF обнулён» — база Δkdf ровно вокруг PUT.
        var before = B18ProfileKdfProbe.Snapshot(_factory.Services);

        // when: успешная смена пароля (верный текущий, валидный новый).
        using var response = await client.PutAsJsonAsync(B18ProfileHarness.PasswordEndpoint, new
        {
            currentPassword = B18ProfileHarness.TestUserPassword,
            password = B18ProfileHarness.NewPassword,
            confirmPassword = B18ProfileHarness.NewPassword,
        });

        // then: 204; Δkdf=2 (verify текущего + hash нового).
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var after = B18ProfileKdfProbe.Snapshot(_factory.Services);
        var delta = B18ProfileKdfProbe.TotalDelta(before, after);
        Assert.True(
            delta == 2,
            $"Ожидался Δkdf=2 (verify+hash), фактически {delta}.");

        // then: вход по новому паролю — 200.
        using var newLoginClient = B18ProfileHarness.Create(_factory);
        using var newPasswordLogin = await B18ProfileHarness.LoginAsync(
            newLoginClient, Login, B18ProfileHarness.NewPassword);
        Assert.True(
            newPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 входа по новому паролю, фактически {(int)newPasswordLogin.StatusCode}: {await newPasswordLogin.Content.ReadAsStringAsync()}");

        // then: вход по старому паролю — 401.
        using var oldLoginClient = B18ProfileHarness.Create(_factory);
        using var oldPasswordLogin = await B18ProfileHarness.LoginAsync(
            oldLoginClient, Login, B18ProfileHarness.TestUserPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 входа по старому паролю, фактически {(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");

        // then: refresh текущей cookie — 204 (токен текущей сессии сохранён, ISS-002).
        using var currentRefreshClient = B18ProfileHarness.Create(_factory);
        using var currentRefresh = await B18ProfileHarness.RefreshAsync(currentRefreshClient, currentDeviceRefresh);
        Assert.True(
            currentRefresh.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204 refresh текущей cookie (сессия сохранена), фактически {(int)currentRefresh.StatusCode}: {await currentRefresh.Content.ReadAsStringAsync()}");

        // then: refresh второго устройства — 401 (токен отозван).
        using var otherRefreshClient = B18ProfileHarness.Create(_factory);
        using var otherRefresh = await B18ProfileHarness.RefreshAsync(otherRefreshClient, otherDeviceRefresh);
        Assert.True(
            otherRefresh.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 refresh второго устройства (токен отозван), фактически {(int)otherRefresh.StatusCode}: {await otherRefresh.Content.ReadAsStringAsync()}");
    }
}
