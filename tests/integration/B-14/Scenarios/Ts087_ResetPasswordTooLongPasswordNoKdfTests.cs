using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-087 «reset-password: сверхдлинный пароль отклоняется, токен жив, 0 KDF»
/// (boundary, FR-014, P1).
///
/// given: живой reset-токен (recovery/request → confirm, код из [DEV-EMAIL]-
///        записи sink); confirmPassword совпадает с password; счётчик KDF
///        «обнулён» — парой снимков IKdfCounter до/после (Δkdf не зависит от
///        накопленных дериваций старта/сида, IF-002);
/// when:  POST /api/v1/auth/reset-password с password длиной 129 символов
///        (содержит цифру, букву, спецзнак — нарушена ТОЛЬКО верхняя граница);
/// then:  400, message 'Данные заполнены неверно', errors.password = РОВНО
///        ['Пароль — не более 128 символов']; токен жив (повтор валидным
///        паролем — 204); Δkdf=0 (FR-014 AC «Сверхдлинный пароль
///        отклоняется»).
/// </summary>
public sealed class Ts087_ResetPasswordTooLongPasswordNoKdfTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "ts087";
    private const string Email = "ts087@example.com";
    private const string FullName = "Студент Восемьдесят Семь";
    private const int TooLongPasswordLength = 129;

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts087_ResetPasswordTooLongPasswordNoKdfTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ResetPassword_WithTooLongPassword_RejectsWithZeroKdf_AndKeepsTokenAlive()
    {
        // given: живой токен; confirmPassword совпадает с password; KDF-счётчик
        // «обнулён» точкой отсчёта; password длиной 129 (цифра, буква, спецзнак).
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
        var tooLongPassword = new string('a', TooLongPasswordLength - 2) + "1!";
        Assert.Equal(TooLongPasswordLength, tooLongPassword.Length);

        var kdfBefore = B14KdfProbe.Snapshot(_factory.Services);

        // when: POST с password длиной 129 (confirmPassword совпадает).
        using var reset = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, tooLongPassword, tooLongPassword);

        // then: 400 'Данные заполнены неверно' + errors.password РОВНО
        // ['Пароль — не более 128 символов'].
        Assert.True(
            reset.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 на пароле длиной 129, фактически " +
            $"{(int)reset.StatusCode}: {await reset.Content.ReadAsStringAsync()}");
        var envelope = await B14Assertions.ReadRootObjectAsync(reset);
        B14Assertions.MessageIs(envelope, B14RecoveryHarness.InvalidDataMessage);
        B14Assertions.ErrorFieldEquals(envelope, "password", "Пароль — не более 128 символов");

        // then: Δkdf=0 (отклонение до деривации).
        var kdfAfter = B14KdfProbe.Snapshot(_factory.Services);
        Assert.True(
            B14KdfProbe.TotalDelta(kdfBefore, kdfAfter) == 0,
            $"Ожидался Δkdf=0 на отклонении сверхдлинного пароля, фактически " +
            $"{B14KdfProbe.TotalDelta(kdfBefore, kdfAfter)}.");

        // then: токен жив (повтор валидным паролем — 204).
        using var retry = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, B14Harness.NewPassword, B14Harness.NewPassword);
        Assert.True(
            retry.StatusCode == HttpStatusCode.NoContent,
            $"Токен обязан остаться живым: ожидался 204, фактически " +
            $"{(int)retry.StatusCode}: {await retry.Content.ReadAsStringAsync()}");
    }
}
