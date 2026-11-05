using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-070 «Код восстановления не попадает в основные логи (ISS-003/SEC-002)»
/// (nfr, FR-012 + NFR-006, P0).
///
/// given: тестовый log-sink разделяет категории (B17LogSink); стенд (а)
///        Development и стенд (б) Production (заданы Auth__JwtKey и
///        нестандартный Seed__TeacherPassword); код извлечён из [DEV-EMAIL]-
///        записи стенда (а) / от счётной обёртки IEmailSender стенда (б).
/// when:  POST recovery/request в обеих конфигурациях; проверка всех записей sink.
/// then:  Development: ни одна запись вне категории 'EmailDev' не содержит код;
///        Production: ни одна запись вообще не содержит код (no-op warning без
///        адресата и содержимого) (FR-012 AC «Код не попадает в основной лог»;
///        NFR-006).
///
/// Методика проверки: сопоставление по ТОЧНОМУ значению кода с границами по
/// цифрам (B17Host.ContainsCode) — не шаблону «6 цифр подряд»: служебные поля
/// записей (durationMs, hex traceId) могут случайно содержать 6 цифр, что давало
/// бы ложные срабатывания (урок CR-001 зоны B-08).
/// </summary>
public sealed class Ts070_RecoveryCodeNotInMainLogsTests
    : IClassFixture<B17WebAppFactory>, IClassFixture<B17ProductionWebAppFactory>
{
    private const string DevelopmentEmail = "student02@example.com";
    private const string ProductionEmail = "student03@example.com";
    private const string DevelopmentRequestBody = """{"email":"student02@example.com"}""";
    private const string ProductionRequestBody = """{"email":"student03@example.com"}""";

    private readonly B17WebAppFactory _development;
    private readonly B17ProductionWebAppFactory _production;

    public Ts070_RecoveryCodeNotInMainLogsTests(
        B17WebAppFactory development,
        B17ProductionWebAppFactory production)
    {
        _development = development;
        _production = production;
    }

    [Fact]
    public async Task RecoveryRequest_InDevelopment_CodeOnlyInEmailDevCategory()
    {
        // given: стенд (а) Development; студент зарегистрирован; sink чист.
        using var client = B17Host.CreateClient(_development);
        B17Host.SeedStudent(_development, "ts070-dev-student", DevelopmentEmail, "Студент Семьдесят Дев");
        _development.LogSink.Clear();

        // when: POST recovery/request в Development.
        using var response = await B17Host.PostJsonAsync(
            client,
            B17Host.RecoveryRequestEndpoint,
            DevelopmentRequestBody);

        // then (premise given): код извлечён из [DEV-EMAIL]-записи — в категории
        //       'EmailDev' есть запись с маркером и 6-значным кодом.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = _development.EmailSpy.SentRecoveryEmails;
        Assert.True(
            sent.Count == 1,
            $"Development: ожидался один вызов IEmailSender, фактически {sent.Count}.");
        var code = sent[0].Code;
        var devEmailRecords = _development.LogSink.OfCategory(B17Host.DevEmailCategory);
        var markerRecords = devEmailRecords
            .Where(record => record.Serialize().Contains(B17Host.DevEmailMarker, StringComparison.Ordinal))
            .ToList();
        Assert.True(
            markerRecords.Any(record => B17Host.ContainsCode(record, code)),
            $"Development: в категории '{B17Host.DevEmailCategory}' нет [DEV-EMAIL]-записи с кодом; "
            + $"записи категории: [{string.Join(" | ", devEmailRecords.Select(r => r.Serialize()))}].");

        // then: Development — ни одна запись вне категории 'EmailDev' не содержит
        //       код (проверяются ВСЕ записи sink, все категории).
        var leaking = _development.LogSink.Snapshot()
            .Where(record => !string.Equals(record.Category, B17Host.DevEmailCategory, StringComparison.Ordinal))
            .Where(record => B17Host.ContainsCode(record, code))
            .ToList();
        Assert.True(
            leaking.Count == 0,
            $"Development: код восстановления найден вне категории '{B17Host.DevEmailCategory}' "
            + $"(ISS-003/SEC-002, NFR-006): [{string.Join(" | ", leaking.Select(r => r.Serialize()))}].");
    }

    [Fact]
    public async Task RecoveryRequest_InProduction_NoRecordContainsCode()
    {
        // given: стенд (б) Production — Auth__JwtKey и нестандартный
        //        Seed__TeacherPassword заданы фикстурой; студент зарегистрирован.
        using var client = B17Host.CreateClient(_production);
        B17Host.SeedStudent(_production, "ts070-prod-student", ProductionEmail, "Студент Семьдесят Дев Прод");
        _production.LogSink.Clear();

        // when: POST recovery/request в Production.
        using var response = await B17Host.PostJsonAsync(
            client,
            B17Host.RecoveryRequestEndpoint,
            ProductionRequestBody);

        // then: 200; ни одна запись вообще не содержит код (точное значение —
        //       от счётной обёртки IEmailSender; в Production код неотслеживаем
        //       из журнала по определению NFR-006).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = _production.EmailSpy.SentRecoveryEmails;
        Assert.True(
            sent.Count == 1,
            $"Production: ожидался один вызов IEmailSender, фактически {sent.Count}.");
        var code = sent[0].Code;
        var leaking = _production.LogSink.Snapshot()
            .Where(record => B17Host.ContainsCode(record, code))
            .ToList();
        Assert.True(
            leaking.Count == 0,
            $"Production: код восстановления найден в журнале (NFR-006: код не появляется вовсе): "
            + $"[{string.Join(" | ", leaking.Select(r => r.Serialize()))}].");
    }
}
