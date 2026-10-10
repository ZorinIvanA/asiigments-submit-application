using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B17.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-069 «recovery/request: несуществующий email — неотличимое поведение»
/// (negative, FR-012, P0).
///
/// given: пользователя с lower(email)='nobody@example.com' не существует; лимит
///        3/час не исчерпан (свежая фикстура — пустой словарь лимитера); тестовый
///        log-sink.
/// when:  POST /auth/recovery/request {email:'nobody@example.com'}.
/// then:  200; Content-Length: 0; в хранилище не появилось кодов; запись
///        'EmailDev' выполнена с тем же форматом, что и для существующего email —
///        маркер [DEV-EMAIL], адресат, 6-значный код (лог не раскрывает
///        существование учётной записи; FR-012 AC «Несуществующий email»).
/// </summary>
public sealed class Ts069_RecoveryRequestUnknownEmailIndistinguishableTests : IClassFixture<B17WebAppFactory>
{
    private const string Email = "nobody@example.com";
    private const string RequestBody = """{"email":"nobody@example.com"}""";

    private readonly B17WebAppFactory _factory;

    public Ts069_RecoveryRequestUnknownEmailIndistinguishableTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RecoveryRequest_UnknownEmail_SameBehaviorAsExisting()
    {
        // given: пользователя с lower(email)='nobody@example.com' не существует.
        using var client = B17Host.CreateClient(_factory);
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        Assert.Null(users.GetByEmail(Email));
        Assert.Equal(0, B17Host.CountRecoveryCodeRecords(_factory));
        _factory.LogSink.Clear();

        // when: POST /auth/recovery/request {email:'nobody@example.com'}.
        using var response = await B17Host.PostJsonAsync(client, B17Host.RecoveryRequestEndpoint, RequestBody);

        // then: 200; Content-Length: 0 (тот же контракт, что для существующего).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await B17Host.AssertEmptyBodyWithZeroContentLengthAsync(response, "recovery/request (несуществующий email)");

        // then: в хранилище не появилось кодов.
        Assert.Equal(
            0,
            B17Host.CountRecoveryCodeRecords(_factory));

        // then: отправка выполнена так же, как для существующего email:
        //       IEmailSender — один раз; запись 'EmailDev' с тем же форматом —
        //       маркер [DEV-EMAIL], адресат, 6-значный код.
        var sent = _factory.EmailSpy.SentRecoveryEmails;
        Assert.True(
            sent.Count == 1,
            $"Ожидался ровно один вызов IEmailSender и для несуществующего email, фактически {sent.Count}.");
        var code = sent[0].Code;
        Assert.True(
            Regex.IsMatch(code, "^[0-9]{6}$", RegexOptions.CultureInvariant),
            $"Ожидался 6-значный код (ASCII-цифры), фактически «{code}».");
        Assert.True(
            sent[0].Email.Equals(Email, StringComparison.OrdinalIgnoreCase),
            $"Ожидался адресат письма «{Email}», фактически «{sent[0].Email}».");
        var devEmailRecords = _factory.LogSink.OfCategory(B17Host.DevEmailCategory);
        var withCode = devEmailRecords.Where(record => B17Host.ContainsCode(record, code)).ToList();
        Assert.True(
            withCode.Count == 1,
            $"В категории '{B17Host.DevEmailCategory}' ожидалась ровно одна запись письма с кодом "
            + $"(формат тот же, что для существующего email); фактически {withCode.Count} из "
            + $"{devEmailRecords.Count} записей категории: "
            + $"[{string.Join(" | ", devEmailRecords.Select(r => r.Serialize()))}].");
        var devRecord = withCode[0].Serialize();
        Assert.True(
            devRecord.Contains(B17Host.DevEmailMarker, StringComparison.Ordinal),
            $"Запись письма без маркера {B17Host.DevEmailMarker} (формат должен не отличаться "
            + $"от существующего email): {devRecord}");
        Assert.True(
            devRecord.Contains(Email, StringComparison.OrdinalIgnoreCase),
            $"Запись письма без адресата {Email}: {devRecord}");
    }
}
