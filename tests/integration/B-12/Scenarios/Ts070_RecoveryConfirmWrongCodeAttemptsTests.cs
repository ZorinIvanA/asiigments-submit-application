using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-070 (P0, negative; FR-013) «recovery/confirm: неверный код — attempts
/// инкрементируется».
/// given: живой код с attempts=0; шов хранилища доступен.
/// when:  POST confirm с code '000000'.
/// then:  400 'Код восстановления не подходит'; attempts живого кода = 1.
///        FR-013 AC «Неверный код — попытки считаются».
/// </summary>
public sealed class Ts070_RecoveryConfirmWrongCodeAttemptsTests(B12RecoveryDevSpyHost factory)
    : IClassFixture<B12RecoveryDevSpyHost>
{
    private const string StudentEmail = "student01@example.com";

    private readonly B12RecoveryDevSpyHost _factory = factory;

    [Fact]
    public async Task TS070_Confirm_WithWrongCode_IncrementsAttemptsOfLiveCode()
    {
        // given: живой код с attempts=0 (DI-сид через шов хранилища).
        var user = B12RecoveryStore.AddStudent(_factory, "student01", StudentEmail);
        var codeRecord = B12RecoveryStore.AddRecoveryCode(_factory, user.Id, "654321");
        var securityTokens = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var client = B12RecoveryHttp.CreateClient(_factory);

        // when: POST confirm с code '000000'.
        using var confirm = await B12RecoveryHarness.ConfirmAsync(client, StudentEmail, "000000");

        // then: 400 'Код восстановления не подходит'.
        await ApiAssert.AssertMessageAsync(confirm, HttpStatusCode.BadRequest, ErrorTexts.RecoveryCodeRejected);

        // then: attempts живого кода = 1 (код остался жив).
        var live = securityTokens.FindLiveForUser(user.Id);
        Assert.NotNull(live);
        Assert.Equal(1, live.Attempts);
        Assert.Equal(codeRecord.Id, live.Id);
        Assert.Null(live.UsedAt);
    }
}
