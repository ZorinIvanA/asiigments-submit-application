using LabsApp.IntegrationTests.B17.Infrastructure;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-190 «NFR-006: Production-конфигурация — код восстановления отсутствует
/// в логах вовсе» (nfr, NFR-006 + FR-012, P0).
///
/// given: Production-стенд (заданы Auth__JwtKey и нестандартный
///        Seed__TeacherPassword — фикстура B17ProductionWebAppFactory);
///        кастомный log-sink (B17LogSink).
/// when:  POST recovery/request для существующего email; проверка всех записей sink.
/// then:  ни одна запись не содержит код; фиксируется не более одного
///        warning-сообщения dev-заглушки без адресата и содержимого
///        (NFR-006 verification (б)).
///
/// Значение кода для поиска — от счётной обёртки IEmailSender (в Production код
/// из журнала неотслеживаем по определению NFR-006). Проверки warning-записей:
/// каждая запись уровня Warning не содержит ни кода (содержимое), ни адресата;
/// всего Warning-записей за сценарий — не более одной (sink очищен перед
/// запросом, успешный 200 не порождает событий Api.Security).
/// </summary>
public sealed class Ts190_ProductionRecoveryCodeNeverInLogsTests : IClassFixture<B17ProductionWebAppFactory>
{
    private const string Login = "ts190-student";
    private const string Email = "student06@example.com";
    private const string FullName = "Студент Сто Девяносто";
    private const string RequestBody = """{"email":"student06@example.com"}""";

    private readonly B17ProductionWebAppFactory _factory;

    public Ts190_ProductionRecoveryCodeNeverInLogsTests(B17ProductionWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RecoveryRequest_InProduction_NoRecordContainsCode_AtMostOneSanitizedWarning()
    {
        // given: Production-стенд; существующий email; sink чист.
        using var client = B17Host.CreateClient(_factory);
        B17Host.SeedStudent(_factory, Login, Email, FullName);
        _factory.LogSink.Clear();

        // when: POST recovery/request для существующего email.
        using var response = await B17Host.PostJsonAsync(
            client, B17Host.RecoveryRequestEndpoint, RequestBody);

        // then: 200 (контракт FR-012 одинаков во всех средах).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await B17Host.AssertEmptyBodyWithZeroContentLengthAsync(response, "recovery/request (Production)");

        // then: ни одна запись не содержит код (точное значение — от счётной
        //       обёртки IEmailSender; поиск по ВСЕМ записям sink).
        var sent = _factory.EmailSpy.SentRecoveryEmails;
        Assert.True(
            sent.Count == 1,
            $"Production: ожидался один вызов IEmailSender, фактически {sent.Count}.");
        var code = sent[0].Code;
        var leaking = _factory.LogSink.Snapshot()
            .Where(record => B17Host.ContainsCode(record, code))
            .ToList();
        Assert.True(
            leaking.Count == 0,
            $"Production: код восстановления найден в журнале (NFR-006: код не появляется вовсе): "
            + $"[{string.Join(" | ", leaking.Select(r => r.Serialize()))}].");

        // then: warning-сообщений — не более одного, и каждое без адресата и
        //       содержимого (no-op warning dev-заглушки, IF-005/NFR-006).
        var warnings = _factory.LogSink.OfLevelWarning();
        Assert.True(
            warnings.Count <= 1,
            $"Production: ожидалось не более одного warning-сообщения dev-заглушки, фактически "
            + $"{warnings.Count}: [{string.Join(" | ", warnings.Select(r => r.Serialize()))}].");
        foreach (var warning in warnings)
        {
            var serialized = warning.Serialize();
            Assert.True(
                !B17Host.ContainsCode(warning, code),
                $"Warning-запись содержит код (NFR-006): {serialized}");
            Assert.True(
                !serialized.Contains(Email, StringComparison.OrdinalIgnoreCase),
                $"Warning-запись содержит адресата (NFR-006: warning без адресата и содержимого): {serialized}");
        }
    }
}
