using System.Text;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B17.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-071 «recovery/request: переотправка гасит прежний код»
/// (data_integrity, FR-012 + FR-013, P0).
///
/// given: живой код C1 пользователя student04@example.com (первый recovery/request).
/// when:  повторный POST recovery/request того же email; затем POST
///        /auth/recovery/confirm с кодом C1.
/// then:  повторный request — 200; C1.usedAt≠null; новый код C2 жив;
///        подтверждение C1 — 400 «Код восстановления не подходит»
///        (FR-012 AC «Переотправка гасит прежний код»).
///
/// Сопоставление C1/C2 с записями хранилища — по Id живой записи,
/// зафиксированной после первого запроса (значения кодов не хранятся — только
/// CodeHash; идентичность подтверждается через Id записей, а код C1 для
/// confirm берётся из счётной обёртки IEmailSender).
/// </summary>
public sealed class Ts071_RecoveryResendKillsPreviousCodeTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts071-student";
    private const string Email = "student04@example.com";
    private const string FullName = "Студент СемьдесятОдин";
    private const string RequestBody = """{"email":"student04@example.com"}""";

    private readonly B17WebAppFactory _factory;

    public Ts071_RecoveryResendKillsPreviousCodeTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Resend_MarksPreviousCodeUsedAndConfirmWithOldCodeIsRejected()
    {
        // given: живой код C1 пользователя.
        using var client = B17Host.CreateClient(_factory);
        var user = B17Host.SeedStudent(_factory, Login, Email, FullName);
        var codes = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        _factory.LogSink.Clear();

        using var firstResponse = await B17Host.PostJsonAsync(
            client, B17Host.RecoveryRequestEndpoint, RequestBody);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var sent = _factory.EmailSpy.SentRecoveryEmails;
        Assert.True(sent.Count == 1, $"Ожидался один вызов IEmailSender, фактически {sent.Count}.");
        var codeC1 = sent[0].Code;
        var liveC1 = codes.FindLiveForUser(user.Id);
        Assert.NotNull(liveC1);
        Assert.Null(liveC1.UsedAt);
        var c1RecordId = liveC1.Id;

        // when: повторный POST recovery/request того же email.
        using var resendResponse = await B17Host.PostJsonAsync(
            client, B17Host.RecoveryRequestEndpoint, RequestBody);

        // then: повторный request — 200; C1.usedAt≠null; новый код C2 жив.
        Assert.Equal(HttpStatusCode.OK, resendResponse.StatusCode);
        var recordsAfterResend = B17Host.GetAllRecoveryCodeRecords(_factory);
        Assert.True(
            recordsAfterResend.Count == 2,
            $"Ожидалось ровно две записи RecoveryCode (C1 + C2), фактически {recordsAfterResend.Count}: "
            + $"[{string.Join(" | ", recordsAfterResend.Select(r => $"{r.Id} usedAt={r.UsedAt:O}"))}].");
        var c1Record = recordsAfterResend.Single(record => record.Id == c1RecordId);
        Assert.True(
            c1Record.UsedAt is not null,
            $"Ожидалось C1.usedAt≠null после переотправки, фактически usedAt={c1Record.UsedAt:O}.");
        var liveC2 = codes.FindLiveForUser(user.Id);
        Assert.NotNull(liveC2);
        Assert.True(
            liveC2.Id != c1RecordId,
            $"Живой код после переотправки совпадает с C1 (Id={c1RecordId}) — новый код C2 не создан.");
        Assert.Null(liveC2.UsedAt);
        Assert.True(
            _factory.EmailSpy.SentRecoveryEmails.Count == 2,
            $"Ожидалось два вызова IEmailSender (C1 и C2), фактически {_factory.EmailSpy.RecoverySendsCount}.");

        // when: POST /auth/recovery/confirm с кодом C1.
        var confirmBody = new StringBuilder("""{"email":""")
            .Append('"').Append(Email).Append('"')
            .Append(""","code":""")
            .Append('"').Append(codeC1).Append('"')
            .Append('}')
            .ToString();
        using var confirmResponse = await B17Host.PostJsonAsync(
            client, B17Host.RecoveryConfirmEndpoint, confirmBody);

        // then: подтверждение C1 — 400 «Код восстановления не подходит».
        Assert.Equal(HttpStatusCode.BadRequest, confirmResponse.StatusCode);
        var content = await confirmResponse.Content.ReadAsStringAsync();
        var message = B17Host.ExtractEnvelopeMessage(content, "recovery/confirm с погашенным C1");
        Assert.True(
            message.Equals(B17Host.CodeRejectedMessage, StringComparison.Ordinal),
            $"Ожидался message «{B17Host.CodeRejectedMessage}» дословно, фактически «{message}».");
    }
}
