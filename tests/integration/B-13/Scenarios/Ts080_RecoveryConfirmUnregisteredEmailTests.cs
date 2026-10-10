using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-080 (P0, negative; FR-013) «Recovery/confirm: незарегистрированный email —
/// тот же текст».
/// given: пользователя с email 'no@no.no' не существует (свежий хост фикстуры).
/// when:  POST confirm {email:'no@no.no', code:'123456'}.
/// then:  400 'Код восстановления не подходит' (без раскрытия существования;
///        FR-013 AC «Незарегистрированный email»).
/// </summary>
public sealed class Ts080_RecoveryConfirmUnregisteredEmailTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS080_Confirm_ForUnregisteredEmail_ReturnsSameRejectionText()
    {
        // given: пользователя 'no@no.no' не существует (свежий хост, сидов нет).
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST confirm {email:'no@no.no', code:'123456'}.
        using var confirm = await B13RecoveryApi.ConfirmAsync(client, "no@no.no", "123456");

        // then: 400 'Код восстановления не подходит' — тот же текст, что и для
        // зарегистрированного email (без раскрытия существования, CODE_REJECTED).
        await ApiAssert.AssertMessageAsync(
            confirm, HttpStatusCode.BadRequest, ErrorTexts.RecoveryCodeRejected);
    }
}
