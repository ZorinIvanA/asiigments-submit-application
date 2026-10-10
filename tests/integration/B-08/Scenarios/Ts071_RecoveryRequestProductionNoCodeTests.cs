using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Infrastructure;
using LabsApp.Observability;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-071 «Production: событие запроса кода без самого кода» (nfr,
/// FR-017 + NFR-006, P0).
///
/// given: Production-хост (валидные секреты — B08ProductionWebAppFactory);
///        пользователь с email a@b.ru (DI-сид).
/// when:  POST /auth/recovery/request; инспекция всех категорий лога.
/// then:  200 с пустым телом (ADR-012); доставка не предусмотрена — ровно
///        один no-op warning категории Hosting.Configuration (ProductionEmailSender)
///        без адресата и содержимого; ни адрес, ни 6-значный код в app-категориях
///        не появляются; категория EmailDev в Production не пишется вовсе.
///        Глоссарий «IEmailSender (заглушка)»; NFR-006: «в средах ≠ Development
///        код не появляется вовсе (no-op warning без содержимого)».
///
/// Примечание к шаблону 6 цифр (доработка CR-001): проверки ведутся по
/// категориям ПРИЛОЖЕНИЯ — глобальный подсчёт «записей вне Api.Request»
/// неисполним против корректной реализации, потому что на каждый HTTP-запрос
/// фреймворк ASP.NET Core пишет собственные Information-записи Microsoft.*
/// (Hosting.Diagnostics, Routing.EndpointMiddleware, …), а appsettings-фильтра
/// «Microsoft»: «Warning» в LabsApp нет. Служебные поля Api.Request (traceId
/// hex, durationMs) и фреймворковые Microsoft.* исключены из substring-проверки
/// — они могут случайно содержать последовательность из 6 цифр, а носителем
/// кода по IF-005 является исключительно категория доставки письма.
/// </summary>
public sealed class Ts071_RecoveryRequestProductionNoCodeTests : IClassFixture<B08ProductionWebAppFactory>
{
    private const string Login = "ts071-student";
    private const string Email = "a@b.ru";
    private const string FullName = "Студент СемьдесятОдин";
    private const string RequestBody = """{"email":"a@b.ru"}""";

    private readonly B08ProductionWebAppFactory _factory;

    public Ts071_RecoveryRequestProductionNoCodeTests(B08ProductionWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RecoveryRequest_InProduction_LogsRequestEventWithoutCode()
    {
        // given: Production-хост; пользователь с email a@b.ru.
        using var client = B08Host.CreateClient(_factory);
        var user = B08Host.SeedStudent(_factory, Login, Email, FullName);
        _factory.LogSink.Clear();

        // when: POST /auth/recovery/request.
        using var response = await client.PostAsync(B08Host.RecoveryRequestEndpoint, RequestBody);

        // then: 200 {}.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        ResponseAssertions.AssertEmptyBody(body, "recovery/request (Production)");

        // then: доставка в Production не предусмотрена — ровно один no-op
        //       warning категории Hosting.Configuration (ProductionEmailSender)
        //       без адресата и содержимого (NFR-006: код восстановления вне
        //       «EmailDev» не появляется вовсе).
        var emailRecords = _factory.LogSink.OfCategory(ProductionEmailSender.LogCategory);
        Assert.True(
            emailRecords.Count == 1,
            $"Ожидался ровно один no-op warning (категория {ProductionEmailSender.LogCategory}); "
            + $"фактически {emailRecords.Count}: "
            + $"[{string.Join(" | ", emailRecords.Select(r => r.Serialize()))}].");
        Assert.True(
            !emailRecords[0].Serialize().Contains("a@b.ru", StringComparison.OrdinalIgnoreCase),
            $"No-op warning Production не должен содержать адрес: {emailRecords[0].Serialize()}");

        // then: адрес получателя не появился ни в одной app-записи
        //       (CR-001: проверка по категориям приложения — см. примечание
        //       в шапке).
        var appRecords = _factory.LogSink.Snapshot()
            .Where(record => !string.Equals(
                record.Category,
                ObservabilityMiddleware.RequestLogCategory,
                StringComparison.Ordinal))
            .Where(record => !record.Category.StartsWith("Microsoft", StringComparison.Ordinal))
            .ToList();
        Assert.True(
            !appRecords.Any(record => record.Serialize().Contains("a@b.ru", StringComparison.OrdinalIgnoreCase)),
            "Адрес email появился в журнале Production: "
            + $"[{string.Join(" | ", appRecords.Where(r => r.Serialize().Contains("a@b.ru", StringComparison.OrdinalIgnoreCase)).Select(r => r.Serialize()))}] "
            + "(NFR-006: вне Development адресат и содержимое не журналируются).");

        // then: записи с 6 цифрами кода нет — по всем app-категориям, кроме
        //       журнала запросов (Api.Request: служебные поля traceId/durationMs
        //       могут случайно содержать 6 цифр) и фреймворковых Microsoft.*
        //       (не категории приложения, см. CR-001); носитель кода по IF-005 —
        //       исключительно категории доставки письма.
        var appLogRecords = _factory.LogSink.Snapshot()
            .Where(record => !string.Equals(
                record.Category,
                ObservabilityMiddleware.RequestLogCategory,
                StringComparison.Ordinal))
            .Where(record => !record.Category.StartsWith("Microsoft", StringComparison.Ordinal))
            .ToList();
        var withCode = appLogRecords.Where(ResponseAssertions.HasSixDigitRun).ToList();
        Assert.True(
            withCode.Count == 0,
            $"В Production-журнале найдены записи с 6-значным кодом: [{string.Join(" | ", withCode.Select(r => r.Serialize()))}].");

        // then: категория Email.Dev кода не содержит (в Production не пишется вовсе).
        var devEmailRecords = _factory.LogSink.OfCategory(DevEmailSender.LogCategory);
        Assert.True(
            devEmailRecords.Count == 0,
            $"В Production категория Email.Dev должна отсутствовать; фактически {devEmailRecords.Count} записей: "
            + $"[{string.Join("; ", devEmailRecords.Select(r => r.Serialize()))}].");
    }
}
