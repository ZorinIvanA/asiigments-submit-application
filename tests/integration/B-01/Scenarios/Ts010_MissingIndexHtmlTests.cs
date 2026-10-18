using LabsApp.IntegrationTests.B01.Infrastructure;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-010 «Нет index.html — несовпавший маршрут 404 конвертом»
/// (FR-002, FR-023, P1).
/// given: стенд с пустым/отсутствующим каталогом wwwroot.
/// when: GET /works.
/// then: 404 с телом {'message':'Не найдено'}.
/// (В предыдущей нумерации зоны кейс значился как TS-007 — файл переименован
/// по актуальному набору кейсов батча.)
/// </summary>
public sealed class Ts010_MissingIndexHtmlTests
{
    [Fact]
    public async Task GetWorksWithoutWwwroot_ReturnsNotFoundEnvelopeNotServerError()
    {
        // given: стенд с отсутствующим каталогом wwwroot (content root — пустой
        // временный каталог; конфигурация Development валидна без секретов).
        using var factory = new NoWwwrootWebAppFactory();
        using var client = HostClients.Create(factory);

        // when: GET /works — несовпавший маршрут без index.html.
        using var response = await client.GetAsync("/works");

        // then: 404 с телом {'message':'Не найдено'} — не 500 и не пустое тело.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await JsonAssert.ErrorEnvelopeAsync(response, "Не найдено");
    }
}
