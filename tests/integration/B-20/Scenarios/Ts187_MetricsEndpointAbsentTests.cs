using System.Net;
using System.Text.Json;
using LabsApp.IntegrationTests.B20.Infrastructure;

namespace LabsApp.IntegrationTests.B20.Scenarios;

/// <summary>
/// Кейс батча B-20: TS-187 «Scope: Prometheus-эндпойнт метрик отсутствует»
/// (scope, P2; OUT-SCOPE-METRICS).
/// given: Приложение запущено.
/// when:  GET /metrics.
/// then:  404 {'message':'Не найдено'} — отдельного metrics-эндпойнта нет;
///        счётчик KDF — in-memory + лог (out_of_scope: «Prometheus-эндпойнт
///        метрик»; наблюдаемость — TS-180).
/// </summary>
public sealed class Ts187_MetricsEndpointAbsentTests
{
    [Fact]
    public async Task GetMetrics_Returns404_NotFoundEnvelope()
    {
        // given: приложение запущено (анонимный клиент).
        using var factory = new B20ApiFactory();
        using var client = B20AuthSessions.Create(factory);

        // when: GET /metrics.
        using var response = await client.GetAsync("/metrics");

        // then: 404 {'message':'Не найдено'} — отдельного metrics-эндпойнта нет;
        // ответ — JSON-конверт ровно с одним свойством message (не Prometheus-формат).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType,
            ignoreCase: true);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        var properties = body.EnumerateObject().ToList();
        Assert.True(
            properties.Count == 1 && properties[0].Name == "message",
            "then не выполнен: тело ответа GET /metrics — не единый конверт "
            + "{'message':'Не найдено'}: " + body.GetRawText());
        Assert.Equal("Не найдено", properties[0].Value.GetString());
    }
}
