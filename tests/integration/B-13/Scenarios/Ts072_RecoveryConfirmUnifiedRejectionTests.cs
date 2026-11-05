using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-072 (P1, negative; FR-013) «recovery/confirm: незарегистрированный email и
/// неживой код — единый текст».
/// given: пользователя с email 'no@no.no' нет; отдельно — код уже использован
///        (usedAt выставлен успешным confirm).
/// when:  POST {email:'no@no.no', code:'123456'}; отдельно confirm с использованным
///        кодом.
/// then:  оба — 400 'Код восстановления не подходит' (тот же текст, без раскрытия
///        причины). FR-013 AC «Незарегистрированный email».
/// </summary>
public sealed class Ts072_RecoveryConfirmUnifiedRejectionTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string UnknownEmail = "no@no.no";
    private const string StudentEmail = "student01@example.com";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS072_Confirm_UnknownEmailAndUsedCode_ReturnIdenticalRejectionText()
    {
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // given/when: пользователя с no@no.no нет — POST {email:'no@no.no', code:'123456'}.
        using var unknownEmailConfirm = await B13RecoveryApi.ConfirmAsync(client, UnknownEmail, "123456");

        // then: 400 'Код восстановления не подходит'.
        var unknownBody = await ApiAssert.AssertMessageAsync(
            unknownEmailConfirm, HttpStatusCode.BadRequest, ErrorTexts.RecoveryCodeRejected);

        // given (отдельно): код уже использован — usedAt выставлен успешным confirm.
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        B13RecoverySeed.AddRecoveryCode(_factory, user.Id, "999999");
        using var successConfirm = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, "999999");
        _ = await ApiAssert.ReadOkJsonAsync(successConfirm);

        // when: confirm с ИСПОЛЬЗОВАННЫМ кодом.
        using var usedCodeConfirm = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, "999999");

        // then: 400 с ТЕМ ЖЕ текстом, без раскрытия причины (CODE_REJECTED един).
        var usedBody = await ApiAssert.AssertMessageAsync(
            usedCodeConfirm, HttpStatusCode.BadRequest, ErrorTexts.RecoveryCodeRejected);
        Assert.Equal(
            unknownBody.GetProperty("message").GetString(),
            usedBody.GetProperty("message").GetString());
    }
}
