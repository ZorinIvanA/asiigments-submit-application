using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-172 «reset-password: несовпадение confirmPassword — 400, токен не гасится»
/// (negative, FR-014/FR-006, P1; ревью R4c).
///
/// given: студент student01 создан DI-сидом (старый пароль известен); живой
///        resetToken получен через recovery/request → recovery/confirm (код из
///        [DEV-EMAIL]-записи категории 'EmailDev' тестового sink, IF-005);
///        счётчик KDF «сброшен» — измерение дельтами снимков IKdfCounter (IF-002);
/// when:  POST /api/v1/auth/reset-password {resetToken, password:'NewPass1!',
///        confirmPassword:'Other2!'}; затем повтор тем же токеном с совпадающими
///        валидными паролями;
/// then:  первый — 400 'Данные заполнены неверно',
///        errors.confirmPassword = ['Пароли не совпадают'] (дословный текст
///        словаря password.mismatch; ключ — по имени поля confirmPassword
///        контракта входа reset-password; валидация password/confirmPassword по
///        правилам FR-006); токен остался живым — повтор с валидными совпадающими
///        паролями даёт 204 (FR-014: «при полевых ошибках 400 VALIDATION и токен
///        НЕ гасится»); Δkdf=0 на первый запрос (KDF только при успехе).
/// </summary>
public sealed class Ts172_ResetPasswordConfirmMismatchTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Студент Первый";
    private const string MismatchedConfirm = "Other2!";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts172_ResetPasswordConfirmMismatchTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ConfirmMismatch_Returns400_DoesNotConsumeToken_ValidRetryResets()
    {
        // given: студент DI-сидом; живой resetToken; точка отсчёта KDF.
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

        var kdfBefore = B14KdfProbe.Snapshot(_factory.Services);

        // when: сброс с НЕСОВПАДАЮЩИМ confirmPassword.
        using var mismatch = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, B14Harness.NewPassword, MismatchedConfirm);

        // then: 400 'Данные заполнены неверно';
        // errors.confirmPassword = ['Пароли не совпадают'] дословно.
        Assert.True(
            mismatch.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 на несовпадении confirmPassword, фактически " +
            $"{(int)mismatch.StatusCode}: {await mismatch.Content.ReadAsStringAsync()}");
        var envelope = await B14Assertions.ReadRootObjectAsync(mismatch);
        B14Assertions.MessageIs(envelope, B14RecoveryHarness.InvalidDataMessage);
        B14Assertions.ErrorFieldEquals(envelope, "confirmPassword", ErrorTexts.PasswordMismatch);

        // then: Δkdf=0 на первый запрос (KDF только при успехе).
        var kdfAfterMismatch = B14KdfProbe.Snapshot(_factory.Services);
        Assert.True(
            B14KdfProbe.TotalDelta(kdfBefore, kdfAfterMismatch) == 0,
            $"Ожидался Δkdf=0 на отклонённом запросе, фактически " +
            $"{B14KdfProbe.TotalDelta(kdfBefore, kdfAfterMismatch)}.");

        // then: токен остался живым — повтор тем же токеном с валидными
        // совпадающими паролями даёт 204.
        using var retry = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, B14Harness.NewPassword, B14Harness.NewPassword);
        Assert.True(
            retry.StatusCode == HttpStatusCode.NoContent,
            $"Токен обязан остаться живым после полевой ошибки: ожидался 204, фактически " +
            $"{(int)retry.StatusCode}: {await retry.Content.ReadAsStringAsync()}");
    }
}
