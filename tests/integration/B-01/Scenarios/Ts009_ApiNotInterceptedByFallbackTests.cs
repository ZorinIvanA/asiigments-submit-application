using LabsApp.IntegrationTests.B01.Infrastructure;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-009 «API-маршруты не перехватываются fallback» (FR-002, P0).
/// given: wwwroot/index.html существует; запрос выполняется анонимно
/// (аутентификации в приложении нет — эндпойнты API публичны).
/// when: GET /api/v1/labs.
/// then: 200; Content-Type application/json — ответ API-контроллера,
/// а не index.html.
/// (В предыдущей нумерации зоны кейс значился как TS-006 — файл переименован
/// по актуальному набору кейсов батча.)
/// </summary>
public sealed class Ts009_ApiNotInterceptedByFallbackTests
{
    [Fact]
    public async Task GetLabsAnonymous_ReturnsApiControllerResponseNotIndexHtml()
    {
        // given: wwwroot/index.html существует; клиент хоста — анонимный.
        using var factory = new B01WebAppFactory();
        using var client = HostClients.Create(factory);

        // when: GET /api/v1/labs анонимно.
        using var response = await client.GetAsync("/api/v1/labs");

        // then: 200; application/json — маршрут обслужен API-контроллером,
        // SPA fallback его не перехватил (тело — не index.html).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<app-root", body, StringComparison.Ordinal);
    }
}
