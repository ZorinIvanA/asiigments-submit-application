using LabsApp.IntegrationTests.B01.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-003 «Голый GET /swagger в Development приходит в Swagger UI»
/// (FR-002, FR-001, P0).
/// given: Environment=Development; wwwroot/index.html существует.
/// when: GET /swagger (без /index.html).
/// then: конечная точка — Swagger UI с 200; редирект-цепочка допущена
/// (аменда оракула арбитражей a-148/a-152: Swashbuckle 9.x отвечает
/// 301 → /swagger/index.html), и тело НЕ является содержимым
/// wwwroot/index.html (по букве AC FR-002: «в Development те же
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
    public async Task GetSwaggerRoot_InDevelopment_EndsAtSwaggerUi200()
    {
        // given: Environment=Development; wwwroot/index.html существует (TestAssets
        // копируется csproj-целью в content root тестового хоста). Клиент — без
        // авто-редиректов: любая цепочка 3xx проходится явно, по шагам.
        using var factory = new B01WebAppFactory(Environments.Development, null);
        using var client = HostClients.Create(factory);

        // when: GET /swagger (без /index.html).
        using var redirectable = await client.GetAsync("/swagger");

        // then: сразу 200 ИЛИ 3xx-редирект внутри /swagger (аменда a-148/a-152:
        // Swashbuckle 9.x отдаёт 301 → /swagger/index.html; конечная точка —
        // Swagger UI). Чужой адрес или отсутствие Location — ошибка маршрута.
        HttpResponseMessage response = redirectable;
        if (!response.IsSuccessStatusCode)
        {
            Assert.True(
                (int)response.StatusCode >= 300 && (int)response.StatusCode < 400,
                $"Ожидался 200 или 3xx-редирект от GET /swagger в Development, фактически: "
                + $"{(int)response.StatusCode} ({response.ReasonPhrase}).");
            var location = response.Headers.Location?.ToString();
            Assert.False(string.IsNullOrEmpty(location), "3xx от /swagger без заголовка Location.");
            // Location может быть и абсолютным (/swagger/index.html), и относительным
            // (swagger/index.html) — проверка пути по нормализованному виду.
            Assert.True(
                location.TrimStart('/').StartsWith("swagger", StringComparison.Ordinal),
                $"Редирект /swagger ведёт вне зоны Swagger: {location}.");

            response = await client.GetAsync(location);
        }

        // then: конечная точка — 200 от Swagger UI.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Конечная точка редирект-цепочки /swagger — ожидался 200, фактически: "
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
            "Тело конечной точки /swagger совпадает с wwwroot/index.html — маршрут перехвачен SPA fallback'ом.");
        Assert.DoesNotContain(IndexMarker, body, StringComparison.Ordinal);
        if (!ReferenceEquals(response, redirectable))
        {
            response.Dispose();
        }
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
