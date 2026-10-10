using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-097 «Смена пароля отзывает ВСЕ refresh, включая текущее устройство
/// (SEC-006)» (happy_path, FR-022, P0).
///
/// given: пользователь вошёл на двух устройствах A и B — DI-сид пользователя
///        с реальным PBKDF2-хэшем пароля (арбитраж a-017/CR-001), два access-cookie
///        (минт ITokenService, ADR-022) и два действующих refresh-токена
///        (ITokenService.CreateRefreshToken + IRefreshTokenRepository.Add, IF-003/IF-015);
///        refresh-cookie устройств предъявляются только на POST /auth/refresh
///        (Path=/api/v1/auth, IF-004) — модель SEC-006/ASM-020 сохранена.
/// when:  С устройства A: PUT /api/v1/me/password
///        {currentPassword:верный, password:'NewPass1!', confirmPassword:'NewPass1!'}.
/// then:  204; вход старым паролем → 401, новым → 200; POST /auth/refresh с
///        refresh-cookie устройства A → 401 и устройства B → 401 (отзываются ВСЕ,
///        включая текущее устройство); немедленный GET /me/profile с прежней
///        access-cookie устройства A → 200 (access действует до exp).
///        FR-022 AC «Успешная смена отзывает все refresh».
/// </summary>
public sealed class Ts097_PasswordChangeRevokesAllRefreshTests : IClassFixture<B14WebAppFactory>
{
    private const string Login = "ts097";
    private const string Email = "ts097@x.ru";
    private const string FullName = "Студент Девяносто Седьмой";

    private readonly B14WebAppFactory _factory;

    public Ts097_PasswordChangeRevokesAllRefreshTests(B14WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PasswordChange_Returns204_RevokesRefreshOfBothDevices_KeepsAccessUntilExp()
    {
        // given: пользователь; два устройства (две access-cookie, два действующих
        // refresh-токена; refresh-cookie устройства — на /auth/refresh).
        var seeded = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var deviceA = B14Harness.CreateSessionClient(_factory, seeded.Id, UserRoles.Student);
        using var deviceB = B14Harness.CreateSessionClient(_factory, seeded.Id, UserRoles.Student);
        var refreshA = B14Harness.SeedRefreshToken(_factory, seeded.Id);
        var refreshB = B14Harness.SeedRefreshToken(_factory, seeded.Id);

        // when: смена пароля с устройства A (верный текущий, валидный новый).
        using var change = await deviceA.PutAsJsonAsync(B14Harness.PasswordEndpoint, new
        {
            currentPassword = B14Harness.TestUserPassword,
            password = B14Harness.NewPassword,
            confirmPassword = B14Harness.NewPassword,
        });

        // then: 204 (пустое тело).
        Assert.True(
            change.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204, фактически {(int)change.StatusCode}: {await change.Content.ReadAsStringAsync()}");

        // then: вход старым паролем → 401, новым → 200.
        using var oldPasswordLogin = await B14Harness.LoginAsync(
            B14Harness.Create(_factory), Login, B14Harness.TestUserPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);
        using var newPasswordLogin = await B14Harness.LoginAsync(
            B14Harness.Create(_factory), Login, B14Harness.NewPassword);
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);

        // then: POST /auth/refresh с refresh-cookie устройства A → 401
        // (отзывается ВСЕ, включая текущее устройство — SEC-006).
        using var refreshAfterA = await B14Harness.RefreshAsync(B14Harness.Create(_factory), refreshA);
        Assert.True(
            refreshAfterA.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 от /auth/refresh устройства A, фактически {(int)refreshAfterA.StatusCode}: {await refreshAfterA.Content.ReadAsStringAsync()}");

        // then: POST /auth/refresh с refresh-cookie устройства B → 401.
        using var refreshAfterB = await B14Harness.RefreshAsync(B14Harness.Create(_factory), refreshB);
        Assert.True(
            refreshAfterB.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 от /auth/refresh устройства B, фактически {(int)refreshAfterB.StatusCode}: {await refreshAfterB.Content.ReadAsStringAsync()}");

        // then: немедленный GET /me/profile с прежней access-cookie устройства A → 200
        // (access действует до exp, ≤ Auth__AccessTtlMinutes).
        using var profileWithOldAccess = await deviceA.GetAsync(B14Harness.ProfileEndpoint);
        Assert.True(
            profileWithOldAccess.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 от /me/profile с прежней access-cookie устройства A, фактически {(int)profileWithOldAccess.StatusCode}: {await profileWithOldAccess.Content.ReadAsStringAsync()}");
    }
}
