using LabsApp.IntegrationTests.B01.Infrastructure;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-172 «Конверт ошибок: неизвестный маршрут под /api/v1 — 404 'Не найдено'»
/// (FR-023, P0).
/// given: приложение запущено.
/// when: GET /api/v1/nothing.
/// then: 404, тело {'message':'Не найдено'} (AC FR-023 «Неизвестный маршрут API»).
/// (В предыдущей нумерации зоны кейс значился как TS-156 — файл переименован
/// по актуальному набору кейсов батча.)
/// </summary>
public sealed class Ts172_ErrorEnvelopeUnknownApiRouteTests
{
    [Fact]
    public async Task GetUnknownApiRoute_ReturnsNotFoundEnvelope()
    {
        // given: приложение запущено (Development-стенд по умолчанию).
        using var factory = new B01WebAppFactory();
        using var client = HostClients.Create(factory);

        // when: GET /api/v1/nothing — маршрут под /api без сопоставления.
        using var response = await client.GetAsync("/api/v1/nothing");

        // then: 404, {'message':'Не найдено'} — единый конверт FR-023; текст —
        // литерал словаря замороженной спеки, не прод-константа (дрейф словаря
        // должен ловиться тестом, а не проходить мимо сравнения с той же
        // константой приложения).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await JsonAssert.ErrorEnvelopeAsync(response, "Не найдено");
    }
}
