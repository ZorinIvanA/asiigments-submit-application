using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-078 (P0, negative; FR-013) «Recovery/confirm: неверный код — attempts
/// инкрементируется».
/// given: живой код пользователя, attempts=0 (DI-сид через шов хранилища).
/// when:  POST confirm с code '000000'.
/// then:  400 'Код восстановления не подходит'; attempts=1; код остался живым
///        (FR-013 AC «Неверный код — попытки считаются»).
/// </summary>
public sealed class Ts078_RecoveryConfirmWrongCodeIncrementsAttemptsTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentEmail = "student01@example.com";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS078_Confirm_WithWrongCode_IncrementsAttemptsAndKeepsCodeLive()
    {
        // given: живой код пользователя с attempts=0.
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        var codeRecord = B13RecoverySeed.AddRecoveryCode(_factory, user.Id, "654321");
        Assert.Equal(0, codeRecord.Attempts);
        var securityTokens = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST confirm с code '000000'.
        using var confirm = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, "000000");

        // then: 400 'Код восстановления не подходит'.
        await ApiAssert.AssertMessageAsync(
            confirm, HttpStatusCode.BadRequest, ErrorTexts.RecoveryCodeRejected);

        // then: attempts=1; код остался живым (usedAt=null).
        var live = securityTokens.FindLiveForUser(user.Id);
        Assert.NotNull(live);
        Assert.Equal(codeRecord.Id, live.Id);
        Assert.Equal(1, live.Attempts);
        Assert.Null(live.UsedAt);
    }
}
