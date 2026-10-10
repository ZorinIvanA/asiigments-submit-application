using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-077 (P0, happy_path; FR-013) «Recovery/confirm: верный код — resetToken,
/// код погашен, токен жив 15 мин».
/// given: часы T0; живой код 123456 у student01@example.com (DI-сид через шов
///        хранилища: CodeHash — прод-сервис ITokenService.HashRecoveryCode).
/// when:  POST /api/v1/auth/recovery/confirm {email:'student01@example.com',
///        code:'123456'}.
/// then:  200 {resetToken: непустая строка}; код погашен (usedAt≠null);
///        PasswordResetToken жив: expiresAt=T0+15 мин, usedAt=null
///        (FR-013 AC «Верный код»).
/// </summary>
public sealed class Ts077_RecoveryConfirmIssuesResetTokenTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentEmail = "student01@example.com";
    private const string KnownCode = "123456";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS077_Confirm_WithCorrectCode_ReturnsResetTokenConsumesCodeAndIssuesLiveToken()
    {
        // given: часы T0 (инжектируемые, FR-003); живой код 123456 (DI-сид).
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        var codeRecord = B13RecoverySeed.AddRecoveryCode(_factory, user.Id, KnownCode);
        var securityTokens = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST /auth/recovery/confirm {email, code:'123456'}.
        var t0 = _factory.Time.GetUtcNow();
        using var confirm = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, KnownCode);

        // then: 200 {resetToken: непустая строка}.
        var body = await ApiAssert.ReadOkJsonAsync(confirm);
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        var resetToken = body.GetProperty("resetToken").GetString();
        Assert.False(
            string.IsNullOrWhiteSpace(resetToken),
            "Ожидался 200 {resetToken: непустая строка} (FR-013 AC «Верный код»).");

        // then: код погашен (usedAt≠null — инспекция записи по Id независимо
        // от живости, шов зоны B-13).
        var consumed = B13RecoveryCodeSeam.FindByIdIncludingUsed(_factory, codeRecord.Id);
        Assert.NotNull(consumed);
        Assert.NotNull(consumed.UsedAt);

        // then: PasswordResetToken жив: expiresAt=T0+15 мин, usedAt=null
        // (поиск по SHA-256 значения — как в прод-потоке, IF-003).
        var resetRecord = securityTokens.FindLiveResetByHash(B13RecoverySeed.Sha256Hex(resetToken!));
        Assert.NotNull(resetRecord);
        Assert.Null(resetRecord.UsedAt);
        Assert.Equal(t0.AddMinutes(15).UtcDateTime, resetRecord.ExpiresAt);
    }
}
