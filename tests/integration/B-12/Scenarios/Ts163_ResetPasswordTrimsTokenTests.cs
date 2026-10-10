using LabsApp.Auth;
using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-163 (P2, boundary; FR-014) «reset-password: трим resetToken перед поиском по хэшу».
/// given: живой resetToken T для student01 — значение известно тесту (DI-минт:
///        ITokenService.CreatePasswordResetToken + ISecurityTokenRepository.Add,
///        ADR-010); счётчик KDF сброшен (дельты снимков IKdfCounter.Snapshot,
///        IF-002/ADR-031 — снимок ПОСЛЕ сида); старый пароль известен
///        (Passw0rd! — DI-сид с реальным хэшем IPasswordHasher).
/// when:  POST /api/v1/auth/reset-password {resetToken:'  <T>  ' (с пробельным
///        обрамлением), password:'NewPass1!', confirmPassword:'NewPass1!'}.
/// then:  204 с пустым телом — токен триммится до вычисления SHA-256 и поиска
///        (FR-014: «resetToken триммится»); вход старым паролем — 401, новым
///        'NewPass1!' — 200; Δkdf(reset_password)=1.
/// </summary>
public sealed class Ts163_ResetPasswordTrimsTokenTests(B12RecoveryWebAppFactory factory)
    : IClassFixture<B12RecoveryWebAppFactory>
{
    private const string LoginName = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Студент Первый";

    private readonly B12RecoveryWebAppFactory _factory = factory;

    [Fact]
    public async Task ResetPassword_WithWhitespaceFramedToken_NoContent_PasswordSwapped_OneKdf()
    {
        // given: студент student01 со СТАРЫМ паролем (реальный хэш IPasswordHasher, IF-002).
        var student = B12RecoveryHarness.SeedStudentWithPassword(
            _factory, LoginName, FullName, Email, B12RecoveryHarness.TestUserPassword);

        // given: живой resetToken T, значение известно тесту (DI-минт, ADR-010).
        var resetToken = B12RecoveryHarness.SeedLiveResetToken(_factory, student.Id);

        // given: счётчик KDF сброшен — точка отсчёта ПОСЛЕ сида (дельты, IF-002).
        var kdfBefore = B12RecoveryHarness.KdfSnapshot(_factory);
        using var client = B12AuthSessions.CreateClient(_factory);

        // when: сброс пароля с пробельным обрамлением resetToken (пароли валидны по FR-006).
        using var response = await B12RecoveryHarness.ResetPasswordAsync(
            client,
            $"  {resetToken}  ",
            B12RecoveryHarness.NewPassword,
            B12RecoveryHarness.NewPassword);

        // then: 204 с пустым телом — токен триммится до вычисления SHA-256 и поиска
        // (без трима дайджест не совпал бы с хранимым и сброс отклонился бы 400).
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204 (resetToken триммится, FR-014), фактически " +
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.True(
            string.IsNullOrEmpty(await response.Content.ReadAsStringAsync()),
            "Тело 204 обязано быть пустым (FR-014/ASM-016: 204 с пустым телом).");

        // then: вход старым паролем — 401.
        using var oldPasswordLogin = await B12RecoveryHarness.LoginAsync(
            client, LoginName, B12RecoveryHarness.TestUserPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался 401 на вход старым паролем после сброса, фактически " +
            $"{(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");

        // then: вход новым паролем 'NewPass1!' — 200.
        using var newPasswordLogin = await B12RecoveryHarness.LoginAsync(
            client, LoginName, B12RecoveryHarness.NewPassword);
        Assert.True(
            newPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался 200 на вход новым паролем после сброса, фактически " +
            $"{(int)newPasswordLogin.StatusCode}: {await newPasswordLogin.Content.ReadAsStringAsync()}");

        // then: Δkdf(reset_password)=1 (успешный сброс — ровно одна деривация, FR-005/IF-008;
        // входы дают метку login и на дельту reset_password не влияют).
        var kdfAfter = B12RecoveryHarness.KdfSnapshot(_factory);
        var resetDelta = B12RecoveryHarness.KdfCallerDelta(
            kdfBefore, kdfAfter, KdfCallers.ResetPassword);
        Assert.True(
            resetDelta == 1,
            $"Ожидался Δkdf(reset_password)=1, фактически {resetDelta}.");
    }
}
