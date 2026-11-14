using LabsApp.IntegrationTests.B01.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-004 «Swagger исключён из SPA fallback в Production» (ISS-008)
/// (FR-002, FR-001, P0).
/// given: wwwroot/index.html существует; хост в среде Production.
/// when: GET /swagger/index.html и GET /swagger.
/// then: оба — 404 с телом {'message':'Не найдено'}; тело НЕ является содержимым
/// index.html: требование исполняется конверт-проверкой (точный JSON {'message':...},
/// ровно один ключ) плюс содержательной негативной проверкой по маркеру тестовой
/// сборки — та же техника, что в TS-009 (побайтовая сверка с фикстурой удалена
/// как недостижимая ветка при уже гарантируемом конверте — CR-002).
/// (В предыдущей нумерации зоны кейс значился как TS-009; Development-ветка
/// маршрутов — кейсы TS-002 и TS-003, файлы Ts002_SwaggerEnvironmentBoundaryTests
/// и Ts003_SwaggerDevelopmentRootTests.)
/// </summary>
public sealed class Ts004_SwaggerExcludedFromFallbackTests
{
    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger")]
    public async Task GetSwaggerRoutes_InProduction_ReturnEnvelopeNotFoundNeverIndexHtml(string path)
    {
        // given: wwwroot/index.html существует; среда Production (валидные секреты
        // задаёт фабрика по умолчанию).
        using var factory = new B01WebAppFactory(Environments.Production, null);
        using var client = HostClients.Create(factory);

        // when: GET /swagger/index.html | GET /swagger.
        using var response = await client.GetAsync(path);

        // then: 404 с телом {'message':'Не найдено'}; тело — не index.html:
        // конверт-проверка фиксирует точную форму ответа, маркер тестовой сборки
        // даёт самостоятельную негативную диагностику перехвата fallback'ом.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await JsonAssert.ErrorEnvelopeAsync(response, "Не найдено");
        Assert.DoesNotContain("<app-root", body, StringComparison.Ordinal);
    }
}
