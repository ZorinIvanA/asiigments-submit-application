using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;

namespace LabsApp.Tests.Hosting;

/// <summary>
/// Интеграционные проверки hosting-каркаса (FR-001, FR-002, FR-023):
/// /health {"status":"ok"}, 404-конверт {'message':'Не найдено'} для /api/** без
/// маршрута (любая версия/метод) и /swagger* вне Development, статика и
/// SPA-fallback, Swagger только в Development.
/// </summary>
public sealed class HostingEndpointTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private const string NotFoundEnvelope = $"{{\"message\":\"{LabsApp.Hosting.ErrorTexts.NotFound}\"}}";

    private static Task<string> ReadWwwrootFileAsync(params string[] segments) =>
        File.ReadAllTextAsync(Path.Combine([AppContext.BaseDirectory, "wwwroot", .. segments]));

    private static Task<string> ReadIndexHtmlAsync() => ReadWwwrootFileAsync("index.html");

    // ------------------------------------------------------------------
    // FR-001: health-check без аутентификации.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Health_ReturnsOkWithExactJson()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------------
    // FR-001/FR-023: 404-конверт несопоставленных маршрутов под /api.
    // ------------------------------------------------------------------

    [Fact]
    public async Task UnknownApiRoute_ReturnsNotFoundEnvelopeInsteadOfIndexHtml()
    {
        var indexHtml = await ReadIndexHtmlAsync();

        var response = await _client.GetAsync("/api/v1/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(NotFoundEnvelope, body);
        Assert.NotEqual(indexHtml, body);
    }

    [Fact]
    public async Task UnknownApiRoute_UnmappedVersion_ReturnsNotFoundEnvelope()
    {
        // AC FR-001 «Версионированный префикс»: /api/v2/* тоже 404-конверт.
        var response = await _client.GetAsync("/api/v2/anything");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotFoundEnvelope, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownApiRoute_PostMethod_ReturnsNotFoundEnvelope()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/nonexistent", new { value = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotFoundEnvelope, await response.Content.ReadAsStringAsync());
    }

    // Дословные пути требования конвейера: /api/v1/nothing и /api/v2/anything.
    [Theory]
    [InlineData("/api/v1/nothing")]
    [InlineData("/api/v2/anything")]
    public async Task UnknownApiRoute_LiteralRequirementPaths_ReturnNotFoundEnvelope(string path)
    {
        var indexHtml = await ReadIndexHtmlAsync();

        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(NotFoundEnvelope, body);
        Assert.NotEqual(indexHtml, body);
    }

    [Fact]
    public async Task UnknownApiRoute_DeleteMethod_ReturnsNotFoundEnvelope()
    {
        var response = await _client.DeleteAsync("/api/anything");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotFoundEnvelope, await response.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------------
    // FR-002: статика и SPA-fallback с защитными заголовками (SEC-006).
    // ------------------------------------------------------------------

    [Fact]
    public async Task SpaFallback_DeepLink_ReturnsIndexHtml()
    {
        var indexHtml = await ReadIndexHtmlAsync();

        var response = await _client.GetAsync("/works");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(indexHtml, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SpaFallback_RootPath_ReturnsIndexHtml()
    {
        var indexHtml = await ReadIndexHtmlAsync();

        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(indexHtml, await response.Content.ReadAsStringAsync());
    }

    // SEC-006: fallback-ответы (deep-link и корень) несут nosniff и DENY.
    [Theory]
    [InlineData("/works")]
    [InlineData("/")]
    public async Task SpaFallback_CarriesSecurityHeaders(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task StaticFile_ServedAsFileNotIndexHtml()
    {
        var expected = await ReadWwwrootFileAsync("assets", "hosting-smoke.txt");

        var response = await _client.GetAsync("/assets/hosting-smoke.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expected, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StaticFile_CarriesSecurityHeaders()
    {
        var response = await _client.GetAsync("/assets/hosting-smoke.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task StaticFile_Favicon_ServedWithIconMimeAndSecurityHeaders()
    {
        var response = await _client.GetAsync("/favicon.ico");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/x-icon", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task SpaFallback_NonGetRequest_DoesNotReturnIndexHtml()
    {
        var indexHtml = await ReadIndexHtmlAsync();

        var response = await _client.PostAsync("/works", new StringContent(string.Empty));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(indexHtml, await response.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------------
    // FR-001/FR-002 (ISS-008): Swagger только в Development; вне Development
    // /swagger* — 404-конверт, никогда не index.html.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Swagger_AvailableInDevelopment()
    {
        var indexHtml = await ReadIndexHtmlAsync();

        var response = await _client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // FR-001: маршрут обслужен Swagger UI (совпал раньше SPA-fallback),
        // тело — не index.html клиента.
        Assert.NotEqual(indexHtml, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Swagger_BlockedOutsideDevelopment_ReturnsNotFoundEnvelope()
    {
        var indexHtml = await ReadIndexHtmlAsync();
        using var productionFactory = new TestWebAppFactory(Environments.Production);
        using var client = productionFactory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotFoundEnvelope, await response.Content.ReadAsStringAsync());
        Assert.NotEqual(indexHtml, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SwaggerRoot_BlockedOutsideDevelopment_ReturnsNotFoundEnvelope()
    {
        using var productionFactory = new TestWebAppFactory(Environments.Production);
        using var client = productionFactory.CreateClient();

        var response = await client.GetAsync("/swagger");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotFoundEnvelope, await response.Content.ReadAsStringAsync());
    }
}
