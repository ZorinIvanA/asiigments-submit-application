using LabsApp.IntegrationTests.B01.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-003 «Голый GET /swagger в Development отдаёт 200 от Swagger UI»
/// (FR-002, FR-001, P0).
/// given: Environment=Development; wwwroot/index.html существует.
/// when: GET /swagger (без /index.html).
/// then: 200 от Swagger UI — не 3xx-редирект, не 404, и тело НЕ является
/// содержимым wwwroot/index.html (по букве AC FR-002: «в Development те же
/// маршруты — 200 от Swagger UI»).
///
/// Второй тест файла — глубинная проверка тела /swagger/index.html в Development
/// (тело — страница Swagger UI, не index.html), унаследованная из прежней
/// нумерации зоны (бывший TS-010) как регресс-охрана Development-ветки кейса
/// TS-002; маршрутная граница Development/Production — TS-002.
/// </summary>
public sealed class Ts003_SwaggerDevelopmentRootTests
{
    /// <summary>Маркер тестового index.html (TestAssets) — признак перехвата fallback'ом.</summary>
    private const string IndexMarker = "тестовая сборка клиента";

    /// <summary>Маркер страницы Swagger UI (шаблон Swashbuckle: div id="swagger-ui").</summary>
    private const string SwaggerUiMarker = "swagger-ui";

    [Fact]
    public async Task GetSwaggerRoot_InDevelopment_Returns200FromSwaggerUiWithoutRedirect()
    {
        // given: Environment=Development; wwwroot/index.html существует (TestAssets
        // копируется csproj-целью в content root тестового хоста). Клиент — без
        // авто-редиректов: любой 3xx остаётся виден в статусе ответа.
        using var factory = new B01WebAppFactory(Environments.Development, null);
        using var client = HostClients.Create(factory);

        // when: GET /swagger (без /index.html).
        using var response = await client.GetAsync("/swagger");

        // then: 200 от Swagger UI — не 3xx-редирект, не 404.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался 200 от Swagger UI на голый GET /swagger в Development, фактически: "
            + $"{(int)response.StatusCode} ({response.ReasonPhrase}).");

        // then: тело — страница Swagger UI.
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(SwaggerUiMarker, body, StringComparison.Ordinal);

        // then: тело НЕ является содержимым wwwroot/index.html — ни байт-в-байт,
        // ни по маркеру тестовой сборки (маршрут не перехвачен SPA fallback'ом).
        var expectedIndex = await File.ReadAllBytesAsync(Path.Combine(RepoPaths.TestWwwroot, "index.html"));
        var actual = await response.Content.ReadAsByteArrayAsync();
        Assert.False(
            expectedIndex.AsSpan().SequenceEqual(actual),
            "Тело GET /swagger совпадает с wwwroot/index.html — маршрут перехвачен SPA fallback'ом.");
        Assert.DoesNotContain(IndexMarker, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSwaggerIndexHtml_InDevelopment_ReturnsSwaggerUiPageNotIndexHtml()
    {
        // Регресс-охрана Development-ветки кейса TS-002 (глубина тела; прежняя
        // нумерация зоны — TS-010): страница /swagger/index.html — это Swagger UI,
        // а не содержимое wwwroot/index.html.
        using var factory = new B01WebAppFactory(Environments.Development, null);
        using var client = HostClients.Create(factory);

        // when: GET /swagger/index.html.
        using var response = await client.GetAsync("/swagger/index.html");

        // then: 200; Content-Type text/html; тело — страница Swagger UI.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался 200 от Swagger UI, фактически: {(int)response.StatusCode}.");
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(SwaggerUiMarker, body, StringComparison.Ordinal);

        // then: тело НЕ содержимое wwwroot/index.html.
        var expectedIndex = await File.ReadAllBytesAsync(Path.Combine(RepoPaths.TestWwwroot, "index.html"));
        var actual = await response.Content.ReadAsByteArrayAsync();
        Assert.False(
            expectedIndex.AsSpan().SequenceEqual(actual),
            "Тело /swagger/index.html совпадает с wwwroot/index.html — маршрут перехвачен SPA fallback'ом.");
        Assert.DoesNotContain(IndexMarker, body, StringComparison.Ordinal);
    }
}
