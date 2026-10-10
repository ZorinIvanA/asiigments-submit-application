using LabsApp.IntegrationTests.B01.Infrastructure;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-009 «API-маршруты не перехватываются fallback» (FR-002, FR-022, P0).
/// given: wwwroot/index.html существует; запрос без cookie access_token.
/// when: GET /api/v1/labs.
/// then: 401; Content-Type application/json; тело {'message':'Не авторизован'} —
/// JSON-конверт, а не index.html.
/// (В предыдущей нумерации зоны кейс значился как TS-006 — файл переименован
/// по актуальному набору кейсов батча.)
/// </summary>
public sealed class Ts009_ApiNotInterceptedByFallbackTests
{
    [Fact]
    public async Task GetLabsWithoutCookie_ReturnsUnauthorizedEnvelopeNotIndexHtml()
    {
        // given: wwwroot/index.html существует; запрос выполняется без cookie
        // access_token (клиент хоста — без cookie-контейнера сессии).
        using var factory = new B01WebAppFactory();
        using var client = HostClients.Create(factory);

        // when: GET /api/v1/labs анонимно.
        using var response = await client.GetAsync("/api/v1/labs");

        // then: 401; application/json; {'message':'Не авторизован'} — не index.html.
        // Текст — литерал из словаря замороженной спеки (FR-023), не прод-константа:
        // сравнение выхода приложения с его же константой не ловит дрейф словаря.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await JsonAssert.ErrorEnvelopeAsync(response, "Не авторизован");
        Assert.DoesNotContain("<app-root", body, StringComparison.Ordinal);
    }
}
