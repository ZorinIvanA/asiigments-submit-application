using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-071 (P0, boundary; FR-013) «recovery/confirm: пятая неверная попытка
/// аннулирует код».
/// given: живой код с attempts=4; верное значение кода известно тесту.
/// when:  POST confirm с неверным code; затем POST confirm с ВЕРНЫМ code.
/// then:  первый — 400 и код погашен (usedAt≠null); второй — 400 'Код
///        восстановления не подходит'. FR-013 AC «Пятая неверная аннулирует код».
/// </summary>
public sealed class Ts071_RecoveryConfirmFifthAttemptAnnulsTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentEmail = "student01@example.com";
    private const string CorrectCode = "111111";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS071_Confirm_FifthWrongAttemptAnnulsCodeAndEvenCorrectCodeIsRejected()
    {
        // given: живой код с attempts=4 (DI-сид через шов хранилища), верное
        // значение известно тесту.
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        var codeRecord = B13RecoverySeed.AddRecoveryCode(_factory, user.Id, CorrectCode, attempts: 4);
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST confirm с неверным code.
        using var wrongConfirm = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, "000000");

        // then: 400 и код ПОГАШЕН (usedAt≠null — 5-я неверная попытка аннулирует;
        // инспекция неживой записи через шов хранилища).
        await ApiAssert.AssertMessageAsync(wrongConfirm, HttpStatusCode.BadRequest, ErrorTexts.RecoveryCodeRejected);
        var annulled = B13RecoveryCodeSeam.FindByIdIncludingUsed(_factory, codeRecord.Id);
        Assert.NotNull(annulled);
        Assert.NotNull(annulled.UsedAt);

        // when: POST confirm с ВЕРНЫМ code.
        using var correctConfirm = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, CorrectCode);

        // then: 400 'Код восстановления не подходит' (аннулированный код = не живой).
        await ApiAssert.AssertMessageAsync(correctConfirm, HttpStatusCode.BadRequest, ErrorTexts.RecoveryCodeRejected);
    }
}
