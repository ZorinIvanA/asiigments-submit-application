using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-086 «reset-password: полевая ошибка не гасит токен» (negative, FR-014, P0).
///
/// given: живой resetToken (recovery/request → confirm, код из [DEV-EMAIL]-
///        записи sink);
/// when:  POST /api/v1/auth/reset-password с password 'abc' (confirmPassword
///        'abc'); затем повтор с валидным password;
/// then:  первый — 400, message 'Данные заполнены неверно', errors.password
///        содержит тексты min/digit/special ('Пароль должен содержать не менее
///        8 символов', 'Пароль должен содержать хотя бы одну цифру', 'Пароль
///        должен содержать хотя бы один специальный знак'); токен остался
///        живым — повтор с валидным паролем — 204 (FR-014 AC «Полевая ошибка
///        не гасит токен»).
/// </summary>
public sealed class Ts086_ResetPasswordFieldErrorKeepsTokenTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "ts086";
    private const string Email = "ts086@example.com";
    private const string FullName = "Студент Восемьдесят Шесть";
    private const string WeakPassword = "abc";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts086_ResetPasswordFieldErrorKeepsTokenTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ResetPassword_WithFieldError_DoesNotConsumeToken_ValidRetrySucceeds()
    {
        // given: живой resetToken.
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

        // when: POST с password 'abc' (confirmPassword 'abc') — полевая ошибка.
        using var invalid = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, WeakPassword, WeakPassword);

        // then: 400 'Данные заполнены неверно' + errors.password (min/digit/special).
        Assert.True(
            invalid.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 на password 'abc', фактически " +
            $"{(int)invalid.StatusCode}: {await invalid.Content.ReadAsStringAsync()}");
        var envelope = await B14Assertions.ReadRootObjectAsync(invalid);
        B14Assertions.MessageIs(envelope, B14RecoveryHarness.InvalidDataMessage);
        B14Assertions.ErrorFieldContains(
            envelope,
            "password",
            "Пароль должен содержать не менее 8 символов",
            "Пароль должен содержать хотя бы одну цифру",
            "Пароль должен содержать хотя бы один специальный знак");

        // then: токен остался живым — повтор с валидным password → 204.
        using var retry = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, B14Harness.NewPassword, B14Harness.NewPassword);
        Assert.True(
            retry.StatusCode == HttpStatusCode.NoContent,
            $"Токен обязан остаться живым после полевой ошибки: ожидался 204, фактически " +
            $"{(int)retry.StatusCode}: {await retry.Content.ReadAsStringAsync()}");
    }
}
