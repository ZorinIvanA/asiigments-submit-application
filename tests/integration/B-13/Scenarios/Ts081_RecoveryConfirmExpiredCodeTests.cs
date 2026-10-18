using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-081 (P1, negative; FR-013) «Recovery/confirm: просроченный код — не живой,
/// 400».
/// given: код создан в T0; часы переведены на T0+10 мин+1 с (expiresAt прошло),
///        usedAt=null (DI-сид кода с TTL 10 минут, FR-012; инжектируемые часы,
///        FR-003/ADR-002).
/// when:  POST confirm с этим кодом.
/// then:  400 'Код восстановления не подходит' (просроченный код = не живой;
///        FR-013).
/// </summary>
public sealed class Ts081_RecoveryConfirmExpiredCodeTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentEmail = "student01@example.com";
    private const string KnownCode = "123456";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS081_Confirm_WithExpiredCode_IsRejectedAsNotLive()
    {
        // given: код создан в T0 (usedAt=null, TTL 10 минут — FR-012).
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        B13RecoverySeed.AddRecoveryCode(_factory, user.Id, KnownCode);

        // given: часы переведены на T0+10 мин+1 с — expiresAt прошло.
        _factory.Time.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1)));
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST confirm с этим кодом (значение верное, но срок вышел).
        using var confirm = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, KnownCode);

        // then: 400 'Код восстановления не подходит' (просроченный код = не живой).
        await ApiAssert.AssertMessageAsync(
            confirm, HttpStatusCode.BadRequest, ErrorTexts.RecoveryCodeRejected);
    }
}
