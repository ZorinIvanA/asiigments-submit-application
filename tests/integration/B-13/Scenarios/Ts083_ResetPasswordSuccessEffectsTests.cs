using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-083 (P0, happy_path; FR-014/FR-005) «Reset-password: успех — 204, пароль
/// заменён, токены погашены, refresh отозваны, 1 KDF».
/// given: живой resetToken для student01 (пароль 'student123!'); у student01
///        есть активный refresh-токен; счётчик KDF обнулён (дельты снимков,
///        IF-002/ADR-031).
/// when:  POST /api/v1/auth/reset-password {resetToken, password:'NewPass1!',
///        confirmPassword:'NewPass1!'}; затем входы старым и новым паролем;
///        затем /auth/refresh со старым refresh-cookie.
/// then:  204 с пустым телом; вход старым паролем — 401, новым — 200; все
///        reset-токены пользователя погашены; его refresh-токены отозваны
///        (refresh — 401); Δkdf по метке 'reset_password' = 1
///        (FR-014 AC «Успешный сброс»).
/// </summary>
public sealed class Ts083_ResetPasswordSuccessEffectsTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentLogin = "student01";
    private const string StudentEmail = "student01@example.com";
    private const string OldPassword = "student123!";
    private const string NewPassword = "NewPass1!";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS083_ResetPassword_WithLiveToken_ReplacesPasswordConsumesTokensRevokesRefreshOneKdf()
    {
        // given: студент с известным паролем 'student123!' (DI-сид прод-хэшером);
        // живой resetToken, значение известно тесту (DI-минт, ADR-010/ADR-022);
        // активный refresh-токен; счётчик KDF обнулён дельтой снимков.
        var user = B13RecoverySeed.AddStudent(_factory, StudentLogin, StudentEmail, OldPassword);
        var services = _factory.Services;
        var tokens = services.GetRequiredService<ITokenService>();
        var securityTokens = services.GetRequiredService<ISecurityTokenRepository>();
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var refreshGrant = tokens.CreateRefreshToken(user.Id);
        securityTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshGrant.TokenHash,
            ExpiresAt = refreshGrant.ExpiresAt,
            CreatedAt = now,
        });
        var resetToken = B13RecoveryHarness.SeedLiveResetToken(_factory, user.Id);
        var kdfBefore = B13RecoveryHarness.KdfSnapshot(_factory);

        // when: POST /auth/reset-password {resetToken, password, confirmPassword}.
        using var client = B13RecoveryHarness.CreateClient(_factory);
        using var reset = await B13RecoveryApi.ResetPasswordAsync(client, resetToken, NewPassword, NewPassword);

        // then: 204 с пустым телом.
        await B13RecoveryApi.AssertNoContentAsync(reset);

        // then: Δkdf по метке 'reset_password' = 1 (ровно одна деривация).
        var kdfAfter = B13RecoveryHarness.KdfSnapshot(_factory);
        Assert.Equal(1L, B13RecoveryHarness.KdfCallerDelta(kdfBefore, kdfAfter, KdfCallers.ResetPassword));

        // then: вход старым паролем — 401, новым — 200.
        using var oldLogin = await B13RecoveryHarness.LoginAsync(client, StudentLogin, OldPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        using var newLogin = await B13RecoveryHarness.LoginAsync(client, StudentLogin, NewPassword);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        // then: все reset-токены пользователя погашены (предъявленный токен
        // неживой; запись погашена — usedAt≠null, шов хранилища).
        Assert.Null(securityTokens.FindLiveResetByHash(B13RecoverySeed.Sha256Hex(resetToken)));
        var consumed = B13RecoverySeed.ResetTokenByHash(_factory, B13RecoverySeed.Sha256Hex(resetToken));
        Assert.NotNull(consumed);
        Assert.NotNull(consumed.UsedAt);

        // then: refresh-токены отозваны — refresh со старым refresh-cookie даёт 401.
        using var refresh = await B13RecoveryApi.RefreshWithCookieAsync(client, refreshGrant.Value);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }
}
