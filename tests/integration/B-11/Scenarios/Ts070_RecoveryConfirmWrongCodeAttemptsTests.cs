using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B11.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-070 (P0, negative; FR-013) «recovery/confirm: неверный код — attempts
/// инкрементируется».
/// given: живой код с attempts=0; шов хранилища доступен.
/// when:  POST confirm с code '000000'.
/// then:  400 'Код восстановления не подходит'; attempts живого кода = 1.
///        FR-013 AC «Неверный код — попытки считаются».
/// </summary>
public sealed class Ts070_RecoveryConfirmWrongCodeAttemptsTests(B11TimedWebAppFactory factory)
    : IClassFixture<B11TimedWebAppFactory>
{
    private const string StudentEmail = "student01@example.com";

    private readonly B11TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS070_Confirm_WithWrongCode_IncrementsAttemptsOfLiveCode()
    {
        // given: живой код с attempts=0 (DI-сид через шов хранилища).
        var user = B11RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        var codeRecord = B11RecoverySeed.AddRecoveryCode(_factory, user.Id, "654321");
        var securityTokens = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var client = HostClients.Create(_factory);

        // when: POST confirm с code '000000'.
        using var confirm = await B11RecoveryApi.ConfirmAsync(client, StudentEmail, "000000");

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
