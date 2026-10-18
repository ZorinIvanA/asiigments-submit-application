using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B15.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-084 «reset-password: использованный токен отклоняется»
/// (idempotency, FR-014, P1).
///
/// given: resetToken уже успешно использован (recovery/request → confirm →
///        reset-password 204, пароль сброшен);
/// when:  повторный POST /api/v1/auth/reset-password с тем же resetToken
///        (валидные пароли);
/// then:  400 'Ссылка восстановления недействительна или истекла' (одноразовость;
///        повтор не применяет пароль повторно — хранимый хэш по-прежнему
///        соответствует паролю ПЕРВОГО сброса и не соответствует паролю
///        повторной попытки; FR-014).
/// </summary>
public sealed class Ts084_ResetPasswordUsedTokenTests : IClassFixture<B15RecoveryWebAppFactory>
{
    private const string Login = "ts084b15";
    private const string Email = "ts084b15@example.com";
    private const string FullName = "Студент Восемьдесят Четыре";

    /// <summary>Иной ВАЛИДНЫЙ пароль повторной попытки (правила FR-006 соблюдены).</summary>
    private const string RepeatedPassword = "OtherPass2@";

    private readonly B15RecoveryWebAppFactory _factory;

    public Ts084_ResetPasswordUsedTokenTests(B15RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RepeatResetPassword_WithUsedToken_IsRejected_AndDoesNotReapplyPassword()
    {
        // given: resetToken успешно использован (первый сброс — 204, пароль изменён).
        _ = B15RecoveryApi.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student);
        using var client = B15RecoveryApi.CreateClient(_factory);
        var resetToken = await RequestResetTokenAsync(client, Email);

        using var first = await B15RecoveryApi.ResetPasswordAsync(
            client, resetToken, B15RecoveryApi.NewPassword, B15RecoveryApi.NewPassword);
        Assert.True(
            first.StatusCode == HttpStatusCode.NoContent,
            $"Предусловие кейса: первый сброс по живому токену → 204, фактически " +
            $"{(int)first.StatusCode}: {await first.Content.ReadAsStringAsync()}");

        // when: повторный POST с тем же (уже использованным) resetToken.
        using var repeat = await B15RecoveryApi.ResetPasswordAsync(
            client, resetToken, RepeatedPassword, RepeatedPassword);

        // then: 400 'Ссылка восстановления недействительна или истекла' дословно.
        Assert.True(
            repeat.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался 400 на повторном применении использованного токена, фактически " +
            $"{(int)repeat.StatusCode}: {await repeat.Content.ReadAsStringAsync()}");
        BodyAssertions.MessageIs(
            await BodyAssertions.ReadRootObjectAsync(repeat),
            B15RecoveryApi.ResetLinkInvalidMessage);

        // then: повтор не применяет пароль повторно — хранимый хэш соответствует
        //       паролю ПЕРВОГО сброса и НЕ соответствует паролю повторной попытки
        //       (сверка реальным IPasswordHasher хоста, IF-002).
        var storedHash = StoredPasswordHash();
        Assert.True(
            _factory.Services.GetRequiredService<LabsApp.Auth.IPasswordHasher>()
                .Verify(B15RecoveryApi.NewPassword, storedHash, LabsApp.Auth.KdfCallers.Reference),
            "Повтор не должен менять пароль: хранимый хэш обязан соответствовать паролю первого сброса.");
        Assert.False(
            _factory.Services.GetRequiredService<LabsApp.Auth.IPasswordHasher>()
                .Verify(RepeatedPassword, storedHash, LabsApp.Auth.KdfCallers.Reference),
            "Повтор с использованным токеном не должен применять новый пароль.");
    }

    /// <summary>Хранимый хэш пароля пользователя (копия-снимок из IUserRepository, IF-015).</summary>
    private string StoredPasswordHash()
    {
        var user = _factory.Services
            .GetRequiredService<LabsApp.Storage.IUserRepository>()
            .GetByEmail(Email);
        Assert.True(
            user is not null,
            "Пользователь кейса обязан присутствовать в хранилище тестового хоста.");
        return user!.PasswordHash;
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
