using System.Text.Json;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-081 «reset-password: просроченный токен дополнительно гасится»
/// (boundary, FR-014, P0).
///
/// given: живой reset-токен для пользователя (recovery/request → confirm, код из
///        [DEV-EMAIL]-записи sink); инжектируемые часы (FR-003) переведены за
///        expiresAt токена (TTL 15 минут — время сдвинуто на 15 минут и 1 секунду);
/// when:  POST /api/v1/auth/reset-password {resetToken, валидные пароли};
/// then:  400 RESET_LINK_INVALID 'Ссылка восстановления недействительна или
///        истекла'; usedAt токена проставлен — проверяемая наблюдаемо часть:
///        отклонённый токен не «оживает» (повторный сброс им же снова 400 с тем
///        же текстом). Отметка usedAt хранится только внутри хранилища: ни
///        IResetTokenStore (Register/TryConsume), ни IF-015 FindLiveByHash не
///        различают «погашен» и «просрочен» — суб-проверка вынесена в
///        scenario_change_requests (см. артефакт батча).
///        (FR-014 AC «Просроченный токен гасится»).
/// </summary>
public sealed class Ts081_ResetPasswordExpiredTokenTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "ts081";
    private const string Email = "ts081@example.com";
    private const string FullName = "Студент Восемьдесят Один";

    /// <summary>TTL reset-токена — 15 минут (IF-008); проверка — сразу за границей.</summary>
    private static readonly TimeSpan ResetTokenTtl = TimeSpan.FromMinutes(15);

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts081_ResetPasswordExpiredTokenTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ResetPassword_WithExpiredToken_IsRejected_AsResetLinkInvalid()
    {
        // given: живой reset-токен; часы — за expiresAt токена.
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
            && resetTokenProperty.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(resetTokenProperty.GetString()),
            "Предусловие кейса: confirm обязан выдать непустой resetToken.");
        var resetToken = resetTokenProperty.GetString()!;
        _factory.Clock.Advance(ResetTokenTtl.Add(TimeSpan.FromSeconds(1)));

        // when: сброс по просроченному токену.
        using var reset = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, B14Harness.NewPassword, B14Harness.NewPassword);

        // then: 400 RESET_LINK_INVALID, message дословно; отклонённый (погашенный)
        // токен не оживает — повторная попытка также 400 с тем же текстом.
        Assert.True(
            reset.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 на просроченном токене, фактически " +
            $"{(int)reset.StatusCode}: {await reset.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(reset),
            B14RecoveryHarness.ResetLinkInvalidMessage);

        using var repeat = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, B14Harness.NewPassword, B14Harness.NewPassword);
        Assert.True(
            repeat.StatusCode == HttpStatusCode.BadRequest,
            $"Погашенный просроченный токен не должен ожить: ожидался 400, фактически " +
            $"{(int)repeat.StatusCode}: {await repeat.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(repeat),
            B14RecoveryHarness.ResetLinkInvalidMessage);
    }
}
