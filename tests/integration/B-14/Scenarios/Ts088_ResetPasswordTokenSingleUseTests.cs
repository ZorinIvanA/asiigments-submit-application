using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-088 «reset-password: токен одноразовый — повторное использование 400»
/// (idempotency, FR-014, P1).
///
/// given: успешный сброс уже выполнен данным токеном (recovery/request →
///        confirm, код из [DEV-EMAIL]-записи sink; reset-password → 204);
/// when:  повторный POST /api/v1/auth/reset-password с тем же resetToken и
///        валидным новым паролем;
/// then:  400 'Ссылка восстановления недействительна или истекла' (успешный
///        reset гасит все токены пользователя; FR-014).
/// </summary>
public sealed class Ts088_ResetPasswordTokenSingleUseTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "ts088";
    private const string Email = "ts088@example.com";
    private const string FullName = "Студент Восемьдесят Восемь";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts088_ResetPasswordTokenSingleUseTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RepeatResetPassword_WithSameToken_AfterSuccessfulReset_IsRejected()
    {
        // given: успешный сброс уже выполнен данным токеном (→ 204).
        _ = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var client = B14Harness.Create(_factory);
        var code = await B14RecoveryHarness.RequestLiveCodeAsync(_factory, client, Email);
        using var confirm = await B14RecoveryHarness.ConfirmAsync(client, Email, code);
        Assert.True(
            confirm.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: recovery/confirm живым кодом → 200, фактически " +
            $"{(int)confirm.StatusCode}: {await confirm.Content.ReadAsStringAsync()}");
        var confirmBody = await B14Assertions.ReadRootObjectAsync(confirm);
        Assert.True(
            confirmBody.TryGetProperty("resetToken", out var resetTokenProperty)
            && resetTokenProperty.ValueKind == System.Text.Json.JsonValueKind.String
            && !string.IsNullOrWhiteSpace(resetTokenProperty.GetString()),
            "Предусловие кейса: confirm обязан выдать непустой resetToken.");
        var resetToken = resetTokenProperty.GetString()!;

        using var first = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, B14Harness.NewPassword, B14Harness.NewPassword);
        Assert.True(
            first.StatusCode == HttpStatusCode.NoContent,
            $"Предусловие кейса: успешный сброс данным токеном → 204, фактически " +
            $"{(int)first.StatusCode}: {await first.Content.ReadAsStringAsync()}");

        // when: повторный POST с тем же resetToken и валидным новым паролем.
        using var repeat = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, B14Harness.NewPassword, B14Harness.NewPassword);

        // then: 400 'Ссылка восстановления недействительна или истекла'.
        Assert.True(
            repeat.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался 400 на повторном применении использованного токена, фактически " +
            $"{(int)repeat.StatusCode}: {await repeat.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(repeat),
            B14RecoveryHarness.ResetLinkInvalidMessage);
    }
}
