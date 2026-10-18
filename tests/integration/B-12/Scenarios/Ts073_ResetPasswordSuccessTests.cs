using LabsApp.Auth;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-073 (P0, happy_path; FR-014) «reset-password: успех — 204, пароль заменён,
/// токены погашены, refresh отозваны».
/// given: живой resetToken для student01; у пользователя есть второй refresh-токен
///        (другое устройство); счётчик KDF снимком; старый пароль известен.
/// when:  POST /auth/reset-password {resetToken, password:'NewPass1!',
///        confirmPassword:'NewPass1!'}.
/// then:  204 с пустым телом; вход старым паролем — 401, новым — 200; все
///        reset-токены пользователя погашены; все его refresh-токены отозваны
///        (refresh второго устройства — 401); Δkdf(reset_password)=1.
///        FR-014 AC «Успешный сброс».
/// </summary>
public sealed class Ts073_ResetPasswordSuccessTests(B12RecoveryDevSpyHost factory)
    : IClassFixture<B12RecoveryDevSpyHost>
{
    private const string StudentLogin = "student01";
    private const string StudentEmail = "student01@example.com";
    private const string OldPassword = "OldPass1!";
    private const string NewPassword = "NewPass1!";

    private readonly B12RecoveryDevSpyHost _factory = factory;

    [Fact]
    public async Task TS073_ResetPassword_WithLiveToken_ChangesPasswordConsumesResetTokensAndRevokesRefresh()
    {
        // given: студент с ИЗВЕСТНЫМ старым паролем (DI-сид прод-хэшером, метка seed);
        // второй refresh-токен (другое устройство) и дополнительный живой reset-токен;
        // живой resetToken получен через recovery/confirm (кода из шва).
        var user = B12RecoveryStore.AddStudent(_factory, StudentLogin, StudentEmail, OldPassword);
        var services = _factory.Services;
        var securityTokens = services.GetRequiredService<ISecurityTokenRepository>();

        var secondDeviceValue = B12AuthSessions.CreateLiveRefreshToken(_factory, user.Id);
        var extraResetHash = B12RecoveryStore.AddResetToken(_factory, user.Id, "ts073-extra-reset-token");
        B12RecoveryStore.AddRecoveryCode(_factory, user.Id, "444444");

        using var bootstrap = B12RecoveryHttp.CreateClient(_factory);
        using var confirm = await B12RecoveryHarness.ConfirmAsync(bootstrap, StudentEmail, "444444");
        var confirmBody = await ApiAssert.ReadOkJsonAsync(confirm);
        var resetToken = confirmBody.GetProperty("resetToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(resetToken), "given: confirm не выдал resetToken.");

        // счётчик KDF снимком (стартовые деривации сида в Δ не попадают).
        var kdfBefore = B12RecoveryHarness.KdfSnapshot(_factory);

        // when: POST /auth/reset-password {resetToken, password, confirmPassword}.
        using var client = B12RecoveryHttp.CreateClient(_factory);
        using var reset = await B12RecoveryHarness.ResetPasswordAsync(client, resetToken!, NewPassword, NewPassword);

        // then: 204 с пустым телом.
        await B12RecoveryHttp.AssertNoContentAsync(reset);

        // then: Δkdf(reset_password)=1 — ровно одна деривация с меткой reset_password.
        var kdfAfter = B12RecoveryHarness.KdfSnapshot(_factory);
        Assert.Equal(1L, B12RecoveryKdf.TotalDelta(kdfBefore, kdfAfter));
        Assert.Equal(
            1L,
            B12RecoveryHarness.KdfCallerDelta(kdfBefore, kdfAfter, KdfCallers.ResetPassword));

        // then: вход старым паролем — 401, новым — 200.
        using var oldLogin = await B12RecoveryHarness.LoginAsync(client, StudentLogin, OldPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        using var newLogin = await B12RecoveryHarness.LoginAsync(client, StudentLogin, NewPassword);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        // then: ВСЕ reset-токены пользователя погашены (и применённый, и дополнительный).
        Assert.Null(securityTokens.FindLiveResetByHash(B12RecoveryStore.Sha256Hex(resetToken!)));
        Assert.Null(securityTokens.FindLiveResetByHash(extraResetHash));

        // then: ВСЕ refresh-токены отозваны — refresh второго устройства даёт 401.
        using var secondDeviceRefresh = await B12RecoveryHttp.RefreshWithCookieAsync(client, secondDeviceValue);
        Assert.Equal(HttpStatusCode.Unauthorized, secondDeviceRefresh.StatusCode);
    }
}
