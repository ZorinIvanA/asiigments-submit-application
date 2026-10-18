using LabsApp.Auth;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-074 (P0, idempotency; FR-012/FR-013) «Recovery/request: переотправка гасит
/// прежний код».
/// given: живой код C1 пользователя student01@example.com (POST
///        recovery/request + [DEV-EMAIL]-запись тестового sink, IF-005).
/// when:  повторный POST recovery/request того же email; затем POST
///        /auth/recovery/confirm с кодом C1.
/// then:  повторный request — 200; C1.usedAt≠null (погашен); новый код C2 жив;
///        подтверждение C1 — 400 'Код восстановления не подходит'
///        (FR-012 AC «Переотправка гасит прежний код»).
/// </summary>
public sealed class Ts074_RecoveryResendSuppressesPreviousCodeTests(B13RecoveryDevSpyHost factory)
    : IClassFixture<B13RecoveryDevSpyHost>
{
    private const string StudentEmail = "student01@example.com";

    private readonly B13RecoveryDevSpyHost _factory = factory;

    [Fact]
    public async Task TS074_Resend_SuppressesPreviousLiveCode_ConfirmWithItIsRejected()
    {
        // given: живой код C1 (первый запрос; лимит recovery_request 3/час —
        // два запроса в пределах кейса, FR-004).
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        var services = _factory.Services;
        var tokens = services.GetRequiredService<ITokenService>();
        var securityTokens = services.GetRequiredService<ISecurityTokenRepository>();
        using var client = B13RecoveryHarness.CreateClient(_factory);
        using var firstRequest = await B13RecoveryApi.RecoveryRequestAsync(client, StudentEmail);
        await B13RecoveryApi.AssertEmptyBodyOkAsync(firstRequest);
        var c1 = _factory.LogSink.GetLastRecoveryCodeForEmail(StudentEmail);
        var firstLive = securityTokens.FindLiveForUser(user.Id);
        Assert.NotNull(firstLive);
        Assert.Null(firstLive.UsedAt);

        // when: повторный POST recovery/request того же email.
        using var resend = await B13RecoveryApi.RecoveryRequestAsync(client, StudentEmail);

        // then: повторный request — 200 (пустое тело).
        await B13RecoveryApi.AssertEmptyBodyOkAsync(resend);

        // then: C1.usedAt≠null (погашен — инспекция записи по Id независимо от
        // живости, шов зоны B-13); новый код C2 жив.
        var suppressed = B13RecoveryCodeSeam.FindByIdIncludingUsed(_factory, firstLive.Id);
        Assert.NotNull(suppressed);
        Assert.NotNull(suppressed.UsedAt);
        var secondLive = securityTokens.FindLiveForUser(user.Id);
        Assert.NotNull(secondLive);
        Assert.Null(secondLive.UsedAt);
        Assert.NotEqual(firstLive.Id, secondLive.Id);

        // then: значение C2 (из [DEV-EMAIL]-записи) верифицируется хэшем живой записи.
        var resendEntry = B13RecoveryLogAsserts.DevEmailEntries(_factory.LogSink)[^1];
        var c2 = B13RecoveryLogAsserts.VerifiedCode(resendEntry, tokens, secondLive.CodeHash);
        Assert.NotEqual(c1, c2);

        // when: POST /auth/recovery/confirm с кодом C1.
        using var confirmC1 = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, c1);

        // then: 400 'Код восстановления не подходит' — прежний код отвергнут
        // как использованный.
        await ApiAssert.AssertMessageAsync(
            confirmC1, HttpStatusCode.BadRequest, ErrorTexts.RecoveryCodeRejected);
    }
}
