using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-084 (P1, idempotency; FR-014) «Reset-password: одноразовость — повторное
/// использование токена».
/// given: ResetToken уже использован успешным сбросом пароля.
/// when:  Повторный POST /auth/reset-password с тем же токеном и валидным новым
///        паролем.
/// then:  400 RESET_LINK_INVALID 'Ссылка восстановления недействительна или
///        истекла' (успешный reset-password гасит все токены пользователя;
///        использованный токен неживой) (FR-014; PasswordResetToken: live→used).
/// </summary>
public sealed class Ts084_ResetPasswordTokenReuseTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentLogin = "ts084-student";
    private const string StudentEmail = "ts084-student@example.com";
    private const string TokenValue = "ts084-reuse-reset-token";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS084_ResetPassword_ReusedToken_IsRejectedAsInvalidLink()
    {
        // given: ResetToken уже использован успешным сбросом пароля (живой токен
        // DI-сидом, первый POST с валидным паролем даёт 204).
        var user = B13RecoverySeed.AddStudent(_factory, StudentLogin, StudentEmail);
        B13RecoverySeed.AddResetToken(_factory, user.Id, TokenValue);
        using var client = B13RecoveryHarness.CreateClient(_factory);

        using var first = await B13RecoveryApi.ResetPasswordAsync(client, TokenValue, "NewPass1!", "NewPass1!");
        await B13RecoveryApi.AssertNoContentAsync(first);

        // Успешный сброс погасил ВСЕ reset-токены пользователя: запись использованного
        // токена неживая (PasswordResetToken: live→used).
        var consumed = B13RecoverySeed.ResetTokenByHash(_factory, B13RecoverySeed.Sha256Hex(TokenValue));
        Assert.NotNull(consumed);
        Assert.NotNull(consumed.UsedAt);

        // when: Повторный POST /auth/reset-password с тем же токеном и валидным
        // новым паролем.
        using var second = await B13RecoveryApi.ResetPasswordAsync(
            client, TokenValue, "AnotherPass1!", "AnotherPass1!");

        // then: 400 RESET_LINK_INVALID 'Ссылка восстановления недействительна или
        // истекла' (использованный токен неживой — одноразовость).
        await ApiAssert.AssertMessageAsync(second, HttpStatusCode.BadRequest, ErrorTexts.ResetTokenInvalid);
    }
}
