using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B15.Infrastructure;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-190 «NFR-006: Production-конфигурация — код восстановления отсутствует
/// в логах вовсе» (nfr, NFR-006 + FR-012, P0).
///
/// given: Production-стенд (заданы Auth__JwtKey и нестандартный
///        Seed__TeacherPassword — фикстура B15RecoveryProductionWebAppFactory);
///        кастомный log-sink (B15LogSink, очищен перед сценарием);
/// when:  POST recovery/request для существующего email; проверка всех записей sink;
/// then:  ни одна запись не содержит код; фиксируется не более одного
///        warning-сообщения dev-заглушки без адресата и содержимого
///        (NFR-006 verification (б)).
///
/// Точное значение кода для поиска — от счётной обёртки IEmailSender (в
/// Production код из журнала неотслеживаем по определению NFR-006; поиск по
/// ВСЕМ записям sink). Проверки warning-записей: каждая запись уровня Warning
/// не содержит ни кода (содержимого), ни адресата; всего Warning-записей за
/// сценарий — не более одной.
/// </summary>
public sealed class Ts190_ProductionRecoveryCodeNeverInLogsTests
    : IClassFixture<B15RecoveryProductionWebAppFactory>
{
    private const string Login = "ts190b15";
    private const string Email = "ts190b15@example.com";
    private const string FullName = "Студент Сто Девяносто";

    private readonly B15RecoveryProductionWebAppFactory _factory;

    public Ts190_ProductionRecoveryCodeNeverInLogsTests(B15RecoveryProductionWebAppFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task RecoveryRequest_InProduction_NoRecordContainsCode_AtMostOneSanitizedWarning()
    {
        // given: Production-стенд; существующий email (DI-сид); sink чист.
        _ = B15RecoveryApi.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student);
        using var client = B15RecoveryApi.CreateClient(_factory);
        _factory.LogSink.Clear();

        // when: POST recovery/request для существующего email.
        using var response = await B15RecoveryApi.RequestRecoveryCodeAsync(client, Email);

        // then: 200 (контракт FR-012 одинаков во всех средах).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался 200 на recovery/request существующего email (Production), фактически " +
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        // then: точное значение кода — от счётной обёртки IEmailSender (внешняя
        //       система; ожидаем одну отправку).
        var sent = _factory.EmailSpy.SentEmails;
        Assert.True(
            sent.Count == 1,
            $"Production: ожидался один вызов IEmailSender, фактически {sent.Count}.");
        var code = ExtractSixDigitCode(sent[0].Body);

        // then: ни одна запись журнала не содержит код (поиск по ВСЕМ записям sink).
        var leaking = _factory.LogSink.Snapshot()
            .Where(record => record.Serialize().Contains(code, StringComparison.Ordinal))
            .ToList();
        Assert.True(
            leaking.Count == 0,
            $"Production: код восстановления найден в журнале (NFR-006: код не появляется вовсе): " +
            $"[{string.Join(" | ", leaking.Select(record => record.Serialize()))}].");

        // then: warning-сообщений — не более одного, и каждое без адресата и
        //       содержимого (no-op warning dev-заглушки, IF-005/NFR-006).
        var warnings = _factory.LogSink.Snapshot()
            .Where(record => record.Level == LogLevel.Warning)
            .ToList();
        Assert.True(
            warnings.Count <= 1,
            $"Production: ожидалось не более одного warning-сообщения dev-заглушки, фактически " +
            $"{warnings.Count}: [{string.Join(" | ", warnings.Select(record => record.Serialize()))}].");
        foreach (var warning in warnings)
        {
            var serialized = warning.Serialize();
            Assert.True(
                !serialized.Contains(code, StringComparison.Ordinal),
                $"Warning-запись содержит код восстановления (NFR-006): {serialized}");
            Assert.True(
                !serialized.Contains(Email, StringComparison.OrdinalIgnoreCase),
                $"Warning-запись содержит адресата (NFR-006: warning без адресата и содержимого): {serialized}");
        }
    }

    /// <summary>Код восстановления из текста письма: ровно 6 ASCII-цифр (IF-003).</summary>
    private static string ExtractSixDigitCode(string body)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            body,
            @"(?<!\d)\d{6}(?!\d)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        Assert.True(
            match.Success,
            $"Предусловие кейса: в тексте письма не найдено 6-значного кода: «{body}».");
        return match.Value;
    }
}
