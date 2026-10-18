using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Scenarios;

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
public sealed class Ts073_ResetPasswordSuccessTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentLogin = "student01";
    private const string StudentEmail = "student01@example.com";
    private const string OldPassword = "OldPass1!";
    private const string NewPassword = "NewPass1!";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS073_ResetPassword_WithLiveToken_ChangesPasswordConsumesResetTokensAndRevokesRefresh()
    {
        // given: студент с ИЗВЕСТНЫМ старым паролем (DI-сид прод-хэшером, метка seed);
        // второй refresh-токен (другое устройство) и дополнительный живой reset-токен;
        // живой resetToken получен через recovery/confirm (кода из шва).
        var user = B13RecoverySeed.AddStudent(_factory, StudentLogin, StudentEmail, OldPassword);
        var services = _factory.Services;
        var tokens = services.GetRequiredService<ITokenService>();
        var securityTokens = services.GetRequiredService<ISecurityTokenRepository>();
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        var secondDevice = tokens.CreateRefreshToken(user.Id);
        securityTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = secondDevice.TokenHash,
            ExpiresAt = secondDevice.ExpiresAt,
            CreatedAt = now,
        });
        var extraResetHash = B13RecoverySeed.AddResetToken(_factory, user.Id, "ts073-extra-reset-token");
        B13RecoverySeed.AddRecoveryCode(_factory, user.Id, "444444");

        using var bootstrap = B13RecoveryHarness.CreateClient(_factory);
        using var confirm = await B13RecoveryApi.ConfirmAsync(bootstrap, StudentEmail, "444444");
        var confirmBody = await ApiAssert.ReadOkJsonAsync(confirm);
        var resetToken = confirmBody.GetProperty("resetToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(resetToken), "given: confirm не выдал resetToken.");

        // счётчик KDF снимком (стартовые деривации сида в Δ не попадают).
        var kdfBefore = B13RecoveryHarness.KdfSnapshot(_factory);

        // when: POST /auth/reset-password {resetToken, password, confirmPassword}.
        using var client = B13RecoveryHarness.CreateClient(_factory);
        using var reset = await B13RecoveryApi.ResetPasswordAsync(client, resetToken!, NewPassword, NewPassword);

        // then: 204 с пустым телом.
        await B13RecoveryApi.AssertNoContentAsync(reset);

        // then: Δkdf(reset_password)=1 — ровно одна деривация с меткой reset_password.
        var kdfAfter = B13RecoveryHarness.KdfSnapshot(_factory);
        Assert.Equal(1L, B13RecoveryKdf.TotalDelta(kdfBefore, kdfAfter));
        Assert.Equal(1L, B13RecoveryHarness.KdfCallerDelta(kdfBefore, kdfAfter, KdfCallers.ResetPassword));

        // then: вход старым паролем — 401, новым — 200.
        using var oldLogin = await B13RecoveryHarness.LoginAsync(client, StudentLogin, OldPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        using var newLogin = await B13RecoveryHarness.LoginAsync(client, StudentLogin, NewPassword);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        // then: ВСЕ reset-токены пользователя погашены (и применённый, и дополнительный).
        Assert.Null(securityTokens.FindLiveResetByHash(B13RecoverySeed.Sha256Hex(resetToken!)));
        Assert.Null(securityTokens.FindLiveResetByHash(extraResetHash));

        // then: ВСЕ refresh-токены отозваны — refresh второго устройства даёт 401.
        using var secondDeviceRefresh = await B13RecoveryApi.RefreshWithCookieAsync(client, secondDevice.Value);
        Assert.Equal(HttpStatusCode.Unauthorized, secondDeviceRefresh.StatusCode);
    }
}
