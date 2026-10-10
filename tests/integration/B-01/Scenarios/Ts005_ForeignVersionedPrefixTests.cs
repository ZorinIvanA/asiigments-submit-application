using LabsApp.IntegrationTests.B01.Infrastructure;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-005 «Чужой версионированный префикс: /api/v2 → единый 404-конверт»
/// (FR-001, FR-023, P0).
/// given: приложение запущено.
/// when: GET /api/v2/anything.
/// then: 404 с телом {'message':'Не найдено'} (единый конверт ошибок FR-023;
/// FR-001 AC «Версионированный префикс»).
/// (В предыдущей нумерации зоны кейс значился как TS-003 — файл переименован
/// по актуальному набору кейсов батча.)
/// </summary>
public sealed class Ts005_ForeignVersionedPrefixTests
{
    [Fact]
    public async Task GetForeignVersionedPrefix_ReturnsNotFoundAsSingleErrorEnvelope()
    {
        // given: приложение запущено (Development-стенд по умолчанию).
        using var factory = new B01WebAppFactory();
        using var client = HostClients.Create(factory);

        // when: GET /api/v2/anything — префикс вне /api/v1.
        using var response = await client.GetAsync("/api/v2/anything");

        // then: 404 с телом {'message':'Не найдено'} — единый конверт FR-023.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await JsonAssert.ErrorEnvelopeAsync(response, "Не найдено");
    }
}
