using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-069 (P0, happy_path; FR-013) «recovery/confirm: верный код — resetToken
/// наблюдаемой формы, код гасится».
/// given: живой код 123456 у student01@example.com (DI-сид через шов хранилища:
/// CodeHash — прод-сервис ITokenService.HashRecoveryCode); шов хранилища доступен.
/// when:  POST /auth/recovery/confirm {email:'student01@example.com', code:'123456'}.
/// then:  200 {resetToken}; resetToken — непустая строка длиной не менее 43 символов
///        (base64url ≥256 бит); код погашен (usedAt≠null); создан PasswordResetToken
///        со сроком 15 минут — reset-password по нему с валидным паролем даёт 204.
///        FR-013 AC «Верный код»; ASM-002.
/// </summary>
public sealed class Ts069_RecoveryConfirmSuccessTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentEmail = "student01@example.com";
    private const string KnownCode = "123456";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS069_Confirm_WithCorrectCode_IssuesResetTokenOfObservableFormAndConsumesCode()
    {
        // given: живой код 123456 (DI-сид: HashRecoveryCode — прод-хэширование кода).
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        B13RecoverySeed.AddRecoveryCode(_factory, user.Id, KnownCode);
        var securityTokens = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST /auth/recovery/confirm {email, code:'123456'}.
        var confirmedAt = _factory.Time.GetUtcNow();
        using var confirm = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, KnownCode);

        // then: 200 {resetToken: <строка>}; непустая строка длиной не менее 43
        // символов в наблюдаемой форме base64url (≥256 бит, ASM-002).
        var body = await ApiAssert.ReadOkJsonAsync(confirm);
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        var resetToken = body.GetProperty("resetToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(resetToken), "200 без resetToken (FR-013).");
        Assert.True(
            resetToken!.Length >= 43,
            $"resetToken короче 43 символов (base64url 32 байт): {resetToken.Length}.");
        Assert.Matches("^[A-Za-z0-9_-]+$", resetToken);

        // then: код погашен (usedAt≠null — инспекция записи по Id через шов).
        var codes = B13RecoverySeed.AllRecoveryCodes(_factory);
        var codeRecord = Assert.Single(codes, record => record.UserId == user.Id);
        Assert.NotNull(codeRecord.UsedAt);

        // then: создан PasswordResetToken со сроком 15 минут (поиск по SHA-256
        // значения resetToken — как в прод-потоке, IF-003).
        var resetHash = B13RecoverySeed.Sha256Hex(resetToken);
        var resetRecord = securityTokens.FindLiveResetByHash(resetHash);
        Assert.NotNull(resetRecord);
        Assert.Null(resetRecord.UsedAt);
        Assert.Equal(confirmedAt.AddMinutes(15).UtcDateTime, resetRecord.ExpiresAt);

        // then: reset-password по токену с валидным паролем даёт 204.
        using var reset = await B13RecoveryApi.ResetPasswordAsync(client, resetToken, "NewPass1!", "NewPass1!");
        await B13RecoveryApi.AssertNoContentAsync(reset);
    }
}
