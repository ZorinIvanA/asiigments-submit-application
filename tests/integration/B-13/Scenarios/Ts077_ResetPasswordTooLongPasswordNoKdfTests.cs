using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-077 (P0, boundary; FR-014) «reset-password: пароль длиннее 128 отклоняется
/// без KDF».
/// given: живой токен; confirmPassword совпадает с password; счётчик KDF сброшен.
/// when:  POST с password длиной 129.
/// then:  400 'Данные заполнены неверно' + errors.password=['Пароль — не более
///        128 символов']; токен жив; Δkdf=0. FR-014 AC «Сверхдлинный пароль
///        отклоняется» (ISS-016).
/// </summary>
public sealed class Ts077_ResetPasswordTooLongPasswordNoKdfTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentEmail = "student01@example.com";
    private const string TokenValue = "ts077-live-reset-token";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS077_ResetPassword_WithPasswordLongerThan128_RejectedWithoutKdfAndTokenStaysAlive()
    {
        // given: живой токен (confirmPassword совпадает с password); счётчик KDF
        // снимком (Δkdf — между снимками, FR-027).
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        var tokenHash = B13RecoverySeed.AddResetToken(_factory, user.Id, TokenValue);
        var securityTokens = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        var kdfBefore = B13RecoveryHarness.KdfSnapshot(_factory);
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST с password длиной 129 (confirmPassword совпадает).
        var tooLongPassword = new string('a', 129);
        using var reset = await B13RecoveryApi.ResetPasswordAsync(
            client, TokenValue, tooLongPassword, tooLongPassword);

        // then: 400 'Данные заполнены неверно' + errors.password с РОВНО ОНИМ
        // текстом password.max (прочие правила при длине >128 не проверяются,
        // ISS-016/FR-006).
        var body = await ApiAssert.AssertMessageAsync(reset, HttpStatusCode.BadRequest, ErrorTexts.InvalidData);
        var passwordErrors = body.GetProperty("errors").GetProperty("password");
        Assert.Equal(JsonValueKind.Array, passwordErrors.ValueKind);
        var texts = passwordErrors.EnumerateArray().Select(item => item.GetString()).ToArray();
        var maxText = Assert.Single(texts);
        Assert.Equal(ErrorTexts.PasswordMax, maxText);

        // then: токен жив.
        Assert.NotNull(securityTokens.FindLiveResetByHash(tokenHash));

        // then: Δkdf=0 (хэширование не выполнялось — отклонение до KDF).
        var kdfAfter = B13RecoveryHarness.KdfSnapshot(_factory);
        Assert.Equal(0L, B13RecoveryKdf.TotalDelta(kdfBefore, kdfAfter));
    }
}
