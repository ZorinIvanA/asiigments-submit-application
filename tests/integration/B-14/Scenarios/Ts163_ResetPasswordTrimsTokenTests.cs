using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-163 «reset-password: трим resetToken перед поиском по хэшу» (boundary, FR-014, P1).
///
/// given: студент student01 создан DI-сидом (старый пароль известен — 'Passw0rd!');
///        живой resetToken T получен через recovery/request → recovery/confirm
///        (код из [DEV-EMAIL]-записи категории 'EmailDev' тестового sink, IF-005);
///        счётчик KDF «сброшен» — измерение дельтами снимков IKdfCounter (IF-002);
/// when:  POST /api/v1/auth/reset-password {resetToken:'  {T}  ' (с пробельным
///        обрамлением), password:'NewPass1!', confirmPassword:'NewPass1!'};
/// then:  204 с пустым телом — токен триммится до вычисления SHA-256 и поиска
///        (FR-014: «resetToken триммится»); вход старым паролем — 401, новым
///        'NewPass1!' — 200; Δkdf(reset_password)=1.
/// </summary>
public sealed class Ts163_ResetPasswordTrimsTokenTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Студент Первый";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts163_ResetPasswordTrimsTokenTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ResetPassword_WithWhitespaceFramedToken_TrimsToken_AndResets()
    {
        // given: студент DI-сидом (старый пароль известен); живой resetToken T
        // (recovery/request → confirm).
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

        // Счётчик KDF «сброшен»: точка отсчёта перед измеряемым участком (IF-002).
        var kdfBefore = B14KdfProbe.Snapshot(_factory.Services);

        // when: сброс с пробельным обрамлением resetToken.
        using var reset = await B14RecoveryHarness.ResetPasswordAsync(
            client, $"  {resetToken}  ", B14Harness.NewPassword, B14Harness.NewPassword);

        // then: 204 с пустым телом — токен триммится до вычисления SHA-256 и поиска
        // (FR-014: «resetToken триммится»).
        Assert.True(
            reset.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204 на триммированном resetToken, фактически " +
            $"{(int)reset.StatusCode}: {await reset.Content.ReadAsStringAsync()}");
        var resetBody = await reset.Content.ReadAsStringAsync();
        Assert.True(
            string.IsNullOrEmpty(resetBody),
            $"Тело 204 обязано быть пустым, фактически «{resetBody}».");

        // then: Δkdf(reset_password)=1.
        var kdfAfter = B14KdfProbe.Snapshot(_factory.Services);
        Assert.True(
            B14KdfProbe.CallerDelta(kdfBefore, kdfAfter, "reset_password") == 1,
            $"Ожидался Δkdf(reset_password)=1 (FR-014), фактически " +
            $"{B14KdfProbe.CallerDelta(kdfBefore, kdfAfter, "reset_password")}.");

        // then: вход старым паролем — 401, новым 'NewPass1!' — 200.
        using var oldPasswordLogin = await B14Harness.LoginAsync(
            B14Harness.Create(_factory), Login, B14Harness.TestUserPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался 401 на вход старым паролем, фактически " +
            $"{(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");
        using var newPasswordLogin = await B14Harness.LoginAsync(
            B14Harness.Create(_factory), Login, B14Harness.NewPassword);
        Assert.True(
            newPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался 200 на вход новым паролем, фактически " +
            $"{(int)newPasswordLogin.StatusCode}: {await newPasswordLogin.Content.ReadAsStringAsync()}");
    }
}
