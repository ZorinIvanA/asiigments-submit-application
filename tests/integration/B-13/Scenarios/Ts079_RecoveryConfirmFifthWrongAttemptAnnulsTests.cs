using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-079 (P0, boundary; FR-013) «Recovery/confirm: 5-я неверная попытка
/// аннулирует код».
/// given: живой код с attempts=4 (DI-сид через шов хранилища).
/// when:  POST confirm с неверным code; затем POST confirm с ВЕРНЫМ кодом.
/// then:  оба — 400 'Код восстановления не подходит'; после первой попытки код
///        погашен (usedAt≠null) (FR-013 AC «Пятая неверная аннулирует код»).
/// </summary>
public sealed class Ts079_RecoveryConfirmFifthWrongAttemptAnnulsTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentEmail = "student01@example.com";
    private const string CorrectCode = "123456";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS079_Confirm_FifthWrongAttemptAnnulsCode_EvenCorrectValueIsRejectedAfterwards()
    {
        // given: живой код с attempts=4.
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        var codeRecord = B13RecoverySeed.AddRecoveryCode(_factory, user.Id, CorrectCode, attempts: 4);
        Assert.Equal(4, codeRecord.Attempts);
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST confirm с неверным code (пятая неверная попытка).
        using var fifthWrong = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, "000000");

        // then: 400 'Код восстановления не подходит'; код погашен (usedAt≠null —
        // инспекция записи по Id независимо от живости, шов зоны B-13).
        await ApiAssert.AssertMessageAsync(
            fifthWrong, HttpStatusCode.BadRequest, ErrorTexts.RecoveryCodeRejected);
        var annulled = B13RecoveryCodeSeam.FindByIdIncludingUsed(_factory, codeRecord.Id);
        Assert.NotNull(annulled);
        Assert.NotNull(annulled.UsedAt);

        // when: POST confirm с ВЕРНЫМ кодом.
        using var correctAfter = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, CorrectCode);

        // then: тоже 400 'Код восстановления не подходит' — аннулированный код
        // неживой (FR-013: просроченный/использованный код = не живой).
        await ApiAssert.AssertMessageAsync(
            correctAfter, HttpStatusCode.BadRequest, ErrorTexts.RecoveryCodeRejected);
    }
}
