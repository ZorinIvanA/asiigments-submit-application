using LabsApp.Auth;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-067 (P0, data_integrity; FR-012/FR-013) «recovery/request: переотправка
/// гасит прежний код».
/// given: у student01@example.com живой код C1 (получен из записи 'EmailDev');
///        лимит не исчерпан.
/// when:  повторный POST recovery/request того же email; затем POST
///        /auth/recovery/confirm с кодом C1.
/// then:  Request — 200; C1.usedAt≠null (погашен); новый код C2 жив (подтверждение
///        C2 — 200 с resetToken); confirm с C1 — 400 'Код восстановления не
///        подходит'. FR-012 AC «Переотправка гасит прежний код».
/// </summary>
public sealed class Ts067_RecoveryResendSuppressesOldCodeTests(B13RecoveryDevSpyHost factory)
    : IClassFixture<B13RecoveryDevSpyHost>
{
    private const string StudentEmail = "student01@example.com";

    private readonly B13RecoveryDevSpyHost _factory = factory;

    [Fact]
    public async Task TS067_Resend_SuppressesPreviousLiveCodeAndIssuesNewOne()
    {
        // given: живой код C1 (получен из записи 'EmailDev', опознан верификацией
        // по хэшу хранилища); запись кода известна по Id.
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        var tokens = _factory.Services.GetRequiredService<ITokenService>();
        var securityTokens = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var client = B13RecoveryHarness.CreateClient(_factory);

        using var firstRequest = await B13RecoveryApi.RecoveryRequestAsync(client, StudentEmail);
        await B13RecoveryApi.AssertEmptyBodyOkAsync(firstRequest);

        var c1Record = securityTokens.FindLiveForUser(user.Id);
        Assert.NotNull(c1Record);
        var firstDevEmail = Assert.Single(B13RecoveryLogAsserts.DevEmailEntries(_factory.LogSink));
        var c1 = B13RecoveryLogAsserts.VerifiedCode(firstDevEmail, tokens, c1Record.CodeHash);

        // when: повторный POST recovery/request того же email.
        using var resend = await B13RecoveryApi.RecoveryRequestAsync(client, StudentEmail);

        // then: Request — 200; прежний код C1 погашен (usedAt≠null), новый код C2
        // жив (одновременно жив максимум один — инвариант AddLive, IF-015).
        await B13RecoveryApi.AssertEmptyBodyOkAsync(resend);
        var c1AfterResend = B13RecoveryCodeSeam.FindByIdIncludingUsed(_factory, c1Record.Id);
        Assert.NotNull(c1AfterResend);
        Assert.NotNull(c1AfterResend.UsedAt);

        var c2Record = securityTokens.FindLiveForUser(user.Id);
        Assert.NotNull(c2Record);
        Assert.NotEqual(c1Record.Id, c2Record.Id);
        Assert.Null(c2Record.UsedAt);
        var secondDevEmail = B13RecoveryLogAsserts.DevEmailEntries(_factory.LogSink)[1];
        var c2 = B13RecoveryLogAsserts.VerifiedCode(secondDevEmail, tokens, c2Record.CodeHash);
        Assert.NotEqual(c1, c2);

        // then: confirm с C1 — 400 'Код восстановления не подходит' (погашенный код
        // = не живой, FR-013).
        using var confirmOld = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, c1);
        await ApiAssert.AssertMessageAsync(confirmOld, HttpStatusCode.BadRequest, ErrorTexts.RecoveryCodeRejected);

        // then: подтверждение C2 — 200 с resetToken (новый код жив).
        using var confirmNew = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, c2);
        var body = await ApiAssert.ReadOkJsonAsync(confirmNew);
        Assert.False(
            string.IsNullOrWhiteSpace(body.GetProperty("resetToken").GetString()),
            "Подтверждение живого кода C2 ответило без resetToken.");
    }
}
