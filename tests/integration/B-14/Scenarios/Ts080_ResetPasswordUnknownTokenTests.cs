using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-080 «reset-password: несуществующий токен» (negative, FR-014, P0).
///
/// given: пользователь ts080 создан DI-сидом (пароль 'Passw0rd!'); в хранилище
///        нет записи с SHA-256 предъявляемого значения;
/// when:  POST /api/v1/auth/reset-password {resetToken:'garbage',
///        password:'NewPass1!', confirmPassword:'NewPass1!'};
/// then:  400; message 'Ссылка восстановления недействительна или истекла';
///        пароль пользователя не изменён (вход со старым паролем — 200)
///        (FR-014 AC «Несуществующий токен»).
/// </summary>
public sealed class Ts080_ResetPasswordUnknownTokenTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "ts080";
    private const string Email = "ts080@example.com";
    private const string FullName = "Студент Восемьдесят";
    private const string UnknownResetToken = "garbage";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts080_ResetPasswordUnknownTokenTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ResetPassword_WithUnknownToken_IsRejected_AndKeepsOldPassword()
    {
        // given: пользователь; reset-токена с таким значением в хранилище нет.
        _ = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var client = B14Harness.Create(_factory);

        // when: сброс по несуществующему токену.
        using var reset = await B14RecoveryHarness.ResetPasswordAsync(
            client, UnknownResetToken, B14Harness.NewPassword, B14Harness.NewPassword);

        // then: 400; message дословно.
        Assert.True(
            reset.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)reset.StatusCode}: {await reset.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(reset),
            B14RecoveryHarness.ResetLinkInvalidMessage);

        // then: пароль пользователя не изменён — вход со старым паролем работает (200).
        using var oldPasswordLogin = await B14Harness.LoginAsync(
            B14Harness.Create(_factory), Login, B14Harness.TestUserPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Пароль не должен был измениться: вход старым паролем — 200, фактически " +
            $"{(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");
    }
}
