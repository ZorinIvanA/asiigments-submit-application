using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-082 (P2, boundary; FR-013) «Recovery/confirm: email и код триммятся».
/// given: живой код 123456 у student01@example.com (DI-сид через шов хранилища).
/// when:  POST confirm {email:' student01@example.com ', code:' 123456 '}.
/// then:  200 {resetToken} (FR-013: «email и code триммятся»).
/// </summary>
public sealed class Ts082_RecoveryConfirmTrimsEmailAndCodeTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentEmail = "student01@example.com";
    private const string KnownCode = "123456";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS082_Confirm_WithWhitespaceFramedEmailAndCode_TrimmedBeforeMatch()
    {
        // given: живой код 123456 у student01@example.com.
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        B13RecoverySeed.AddRecoveryCode(_factory, user.Id, KnownCode);
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST confirm с пробельным обрамлением ОБЕИХ значений.
        using var confirm = await B13RecoveryApi.ConfirmAsync(
            client, $" {StudentEmail} ", $" {KnownCode} ");

        // then: 200 {resetToken} — email и code триммятся до сверки (без трима
        // значения не совпали бы с хранимыми ci-email и хэшем кода).
        var body = await ApiAssert.ReadOkJsonAsync(confirm);
        var resetToken = body.GetProperty("resetToken").GetString();
        Assert.False(
            string.IsNullOrWhiteSpace(resetToken),
            "Ожидался 200 {resetToken} (FR-013: «email и code триммятся»).");
    }
}
