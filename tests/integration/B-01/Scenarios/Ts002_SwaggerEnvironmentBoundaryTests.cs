using LabsApp.IntegrationTests.B01.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-002 «Swagger UI доступен только в Development» (FR-001, P0).
/// given: два тестовых хоста — ApiFactory (Development) и ProdFactory
/// (Production; Auth__JwtKey задан, Seed__TeacherPassword='Str0ng!Unique2026').
/// when: GET /swagger/index.html на обоих хостах.
/// then: Production — 404; Development — 200 (AC FR-001 «Swagger только в dev»).
///
/// Кейс ограничен маршрутом /swagger/index.html: голый GET /swagger в Development
/// вынесен в отдельный кейс TS-003 (файл Ts003_SwaggerDevelopmentRootTests),
/// проверяющий букву AC FR-002 «200 от Swagger UI» без перенаправления.
/// Production-ветка /swagger/index.html с проверкой формы 404-конверта
/// дополнительно закреплена за TS-004 (не дублируется здесь).
/// </summary>
public sealed class Ts002_SwaggerEnvironmentBoundaryTests
{
    [Fact]
    public async Task GetSwaggerIndexHtml_DevelopmentServes200_ProductionReturns404()
    {
        // given: хост Development (ApiFactory).
        using (var developmentFactory = new B01WebAppFactory(Environments.Development, null))
        using (var developmentClient = HostClients.Create(developmentFactory))
        {
            // when: GET /swagger/index.html в Development.
            using var developmentResponse = await developmentClient.GetAsync("/swagger/index.html");

            // then: 200.
            Assert.True(
                developmentResponse.StatusCode == HttpStatusCode.OK,
                $"Development: ожидался 200 от Swagger UI, фактически: {(int)developmentResponse.StatusCode}.");
        }

        // given: хост Production с секретами из given кейса (Auth__JwtKey задан,
        // Seed__TeacherPassword='Str0ng!Unique2026'; поздние настройки выигрывают).
        var productionSettings = new Dictionary<string, string?>
        {
            ["Auth__JwtKey"] = B01WebAppFactory.ProductionJwtKey,
            ["Seed__TeacherPassword"] = "Str0ng!Unique2026",
        };
        using (var productionFactory = new B01WebAppFactory(Environments.Production, productionSettings))
        using (var productionClient = HostClients.Create(productionFactory))
        {
            // when: GET /swagger/index.html в Production.
            using var productionResponse = await productionClient.GetAsync("/swagger/index.html");

            // then: 404 (AC FR-001 «Swagger только в dev»).
            Assert.True(
                productionResponse.StatusCode == HttpStatusCode.NotFound,
                $"Production: ожидался 404, фактически: {(int)productionResponse.StatusCode}.");
        }
    }
}
