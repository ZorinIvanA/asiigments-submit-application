using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-074 (P0, negative; FR-014) «reset-password: несуществующий токен —
/// RESET_LINK_INVALID».
/// given: в хранилище нет записи с SHA-256 предъявленного значения; пользователь
///        student01 существует со старым паролем.
/// when:  POST {resetToken:'garbage', password:'NewPass1!', confirmPassword:'NewPass1!'}.
/// then:  400 'Ссылка восстановления недействительна или истекла'; пароль не
///        изменён (вход старым паролем — 200). FR-014 AC «Несуществующий токен».
/// </summary>
public sealed class Ts074_ResetPasswordUnknownTokenTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentLogin = "student01";
    private const string StudentEmail = "student01@example.com";
    private const string OldPassword = "OldPass1!";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS074_ResetPassword_WithUnknownToken_IsRejectedAndPasswordUnchanged()
    {
        // given: пользователь student01 существует со старым паролем (DI-сид
        // прод-хэшером); в хранилище нет reset-токенов (свежий хост).
        B13RecoverySeed.AddStudent(_factory, StudentLogin, StudentEmail, OldPassword);
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST {resetToken:'garbage', …валидные пароли…}.
        using var reset = await B13RecoveryApi.ResetPasswordAsync(client, "garbage", "NewPass1!", "NewPass1!");

        // then: 400 'Ссылка восстановления недействительна или истекла'.
        await ApiAssert.AssertMessageAsync(
            reset, HttpStatusCode.BadRequest, ErrorTexts.ResetTokenInvalid);

        // then: пароль не изменён — вход старым паролем даёт 200.
        using var login = await B13RecoveryHarness.LoginAsync(client, StudentLogin, OldPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
