using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-083 «reset-password: сверхдлинный пароль отклоняется без KDF»
/// (boundary, FR-014 + FR-005, P0).
///
/// given: живой reset-токен (recovery/request → confirm, код из [DEV-EMAIL]-
///        записи sink); confirmPassword совпадает с password; точка отсчёта
///        счётчика KDF снята (IF-002);
/// when:  POST /api/v1/auth/reset-password с password длиной 129 символов
///        (буквы + цифра + специальный знак — нарушена ТОЛЬКО верхняя граница);
/// then:  400; message 'Данные заполнены неверно'; errors.password = РОВНО
///        ['Пароль — не более 128 символов']; токен жив (повтор с валидным
///        паролем — 204); Δkdf=0
///        (FR-014 AC «Сверхдлинный пароль отклоняется»).
/// </summary>
public sealed class Ts083_ResetPasswordTooLongPasswordTests : IClassFixture<B15RecoveryWebAppFactory>
{
    private const string Login = "ts083b15";
    private const string Email = "ts083b15@example.com";
    private const string FullName = "Студент Восемьдесят Три";
    private const int TooLongPasswordLength = 129;

    private readonly B15RecoveryWebAppFactory _factory;

    public Ts083_ResetPasswordTooLongPasswordTests(B15RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ResetPassword_WithTooLongPassword_RejectsWithoutKdf_AndKeepsTokenAlive()
    {
        // given: живой reset-токен; точка отсчёта KDF; пароль 129 символов
        // (нарушена только верхняя граница длины — остальные правила соблюдены).
        _ = B15RecoveryApi.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student);
        using var client = B15RecoveryApi.CreateClient(_factory);
        var resetToken = await RequestResetTokenAsync(client, Email);
        var tooLongPassword = new string('a', TooLongPasswordLength - 2) + "1!";
        Assert.Equal(TooLongPasswordLength, tooLongPassword.Length);

        var kdfBefore = B15KdfProbe.Snapshot(_factory.Services);

        // when: сброс со сверхдлинным паролем (confirmPassword совпадает с password).
        using var reset = await B15RecoveryApi.ResetPasswordAsync(
            client, resetToken, tooLongPassword, tooLongPassword);

        // then: 400; message 'Данные заполнены неверно' дословно.
        Assert.True(
            reset.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 на пароле длиной 129, фактически " +
            $"{(int)reset.StatusCode}: {await reset.Content.ReadAsStringAsync()}");
        var envelope = await BodyAssertions.ReadRootObjectAsync(reset);
        BodyAssertions.MessageIs(envelope, B15RecoveryApi.InvalidDataMessage);

        // then: errors.password = РОВНО ['Пароль — не более 128 символов'].
        BodyAssertions.ErrorFieldEquals(envelope, "password", "Пароль — не более 128 символов");

        // then: Δkdf=0 (отклонение до какой-либо деривации — FR-005/IF-002).
        var kdfAfter = B15KdfProbe.Snapshot(_factory.Services);
        Assert.True(
            B15KdfProbe.TotalDelta(kdfBefore, kdfAfter) == 0,
            $"Ожидался Δkdf=0 на отклонении сверхдлинного пароля, фактически " +
            $"{B15KdfProbe.TotalDelta(kdfBefore, kdfAfter)}.");

        // then: токен жив — повтор с валидным паролем → 204.
        using var retry = await B15RecoveryApi.ResetPasswordAsync(
            client, resetToken, B15RecoveryApi.NewPassword, B15RecoveryApi.NewPassword);
        Assert.True(
            retry.StatusCode == HttpStatusCode.NoContent,
            $"Токен обязан остаться живым (полевая ошибка не гасит токен): ожидался 204, " +
            $"фактически {(int)retry.StatusCode}: {await retry.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// Предусловие «живой reset-токен»: recovery/request → 200, код из
    /// [DEV-EMAIL]-записи sink, confirm → 200 {resetToken}.
    /// </summary>
    private async Task<string> RequestResetTokenAsync(HttpClient client, string email)
    {
        using var request = await B15RecoveryApi.RequestRecoveryCodeAsync(client, email);
        Assert.True(
            request.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: POST /auth/recovery/request → 200, фактически " +
            $"{(int)request.StatusCode}: {await request.Content.ReadAsStringAsync()}");
        var code = B15RecoveryLogProbe.GetLastRecoveryCodeForEmail(_factory.LogSink, email);

        using var confirm = await B15RecoveryApi.ConfirmAsync(client, email, code);
        Assert.True(
            confirm.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: recovery/confirm живым кодом → 200, фактически " +
            $"{(int)confirm.StatusCode}: {await confirm.Content.ReadAsStringAsync()}");
        var confirmBody = await BodyAssertions.ReadRootObjectAsync(confirm);
        Assert.True(
            confirmBody.TryGetProperty("resetToken", out var resetTokenProperty)
            && resetTokenProperty.ValueKind == System.Text.Json.JsonValueKind.String
            && !string.IsNullOrWhiteSpace(resetTokenProperty.GetString()),
            "Предусловие кейса: confirm обязан выдать непустой resetToken.");
        return resetTokenProperty.GetString()!;
    }
}
