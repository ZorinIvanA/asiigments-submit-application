using System.Net;
using LabsApp.Hosting;
using LabsApp.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace LabsApp.Tests.Hosting;

/// <summary>
/// Интеграционная верификация конвейера v2.2 (C-001, TestWebAppFactory):
/// fallback-матрица (deep-link / файл / нет index.html / swagger / api),
/// защитные заголовки статики и fallback (SEC-006), 500-конверт необработанных
/// исключений (FR-023), наблюдаемость как первый middleware конвейера.
/// </summary>
public sealed class HostingPipelineTests
{
    private const string NotFoundEnvelope = $"{{\"message\":\"{ErrorTexts.NotFound}\"}}";
    private const string InternalErrorEnvelope = $"{{\"message\":\"{ErrorTexts.InternalServerError}\"}}";

    // ------------------------------------------------------------------
    // Fallback-матрица (FR-002): deep-link → index.html; файл → файл;
    // /api и /swagger → мимо fallback; нет index.html → 404-конверт.
    // ------------------------------------------------------------------

    [Fact]
    public async Task FallbackMatrix_DeepLink_ReturnsIndexHtmlWithSecurityHeaders()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/works");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html")),
            await response.Content.ReadAsStringAsync());
        Assert.Equal("nosniff", response.Headers.GetValues(SecurityHeaders.XContentTypeOptions).Single());
        Assert.Equal(SecurityHeaders.Deny, response.Headers.GetValues(SecurityHeaders.XFrameOptions).Single());
    }

    [Fact]
    public async Task FallbackMatrix_ExistingFile_ServedDirectlyNotIndexHtml()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/assets/hosting-smoke.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "hosting-smoke.txt")),
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FallbackMatrix_ApiPath_NeverInterceptedByFallback()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        // Защищённый и сопоставленный эндпойнт без аутентификации → 401-конверт
        // (AuthN раньше контроллеров); fallback не возвращает index.html.
        var response = await client.GetAsync("/api/v1/labs");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("<html", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FallbackMatrix_SwaggerOutsideDevelopment_ExcludedFromFallback()
    {
        using var factory = new TestWebAppFactory(Environments.Production);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundEnvelope, await response.Content.ReadAsStringAsync());
    }

    // FR-002: пустой wwwroot — несовпавший GET-маршрут 404-конвертом, при этом
    // /health продолжает обслуживаться (API остаётся работоспособен).
    [Fact]
    public async Task FallbackMatrix_MissingIndexHtml_ReturnsNotFoundEnvelope_HealthStillServed()
    {
        var emptyWwwroot = Path.Combine(Path.GetTempPath(), $"labsapp-empty-wwwroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyWwwroot);
        try
        {
            using var factory = new TestWebAppFactory(settings: Settings(
                (WebHostDefaults.WebRootKey, emptyWwwroot)));
            using var client = factory.CreateClient();

            var worksResponse = await client.GetAsync("/works");
            var rootResponse = await client.GetAsync("/");
            var healthResponse = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.NotFound, worksResponse.StatusCode);
            Assert.Equal("application/json", worksResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal(NotFoundEnvelope, await worksResponse.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, rootResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
            Assert.Equal("{\"status\":\"ok\"}", await healthResponse.Content.ReadAsStringAsync());
        }
        finally
        {
            Directory.Delete(emptyWwwroot, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Порядок конвейера (поведенческие пробы): наблюдаемость — первый
    // middleware; route-404 — после маршрутов и до fallback.
    // ------------------------------------------------------------------

    // ObservabilityMiddleware строго первый: запись Api.Request о /health
    // появляется в log-sink тестового хоста (запись делается только из него).
    [Fact]
    public async Task PipelineOrder_ObservabilityIsFirst_RequestLoggedForHealth()
    {
        var factory = new TestWebAppFactory();
        factory.LogSink.Clear();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health?q=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            factory.LogSink.Snapshot(),
            record => record.Category == ObservabilityMiddleware.RequestLogCategory
                && record.Message.Contains("/health", StringComparison.Ordinal));
    }

    // Route-404 раньше SPA-fallback: несопоставленный /api — JSON-конверт,
    // а не index.html.
    [Fact]
    public async Task PipelineOrder_UnmatchedApiRoute_EnvelopeNotIndexHtml()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/definitely-unmapped");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundEnvelope, await response.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------------
    // FR-023: необработанное исключение → 500-конверт без деталей.
    // ------------------------------------------------------------------

    [Fact]
    public async Task UnhandledException_Returns500EnvelopeWithoutDetails()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/host-smoke/throw");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(InternalErrorEnvelope, body);
        Assert.DoesNotContain(HostingSmokeController.ThrownDetail, body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // FR-001: CORS не настраивается — ни один ответ (включая ответы на запросы
    // с Origin и preflight-OPTIONS) не содержит Access-Control-* заголовков.
    // ------------------------------------------------------------------

    // Имена ответных CORS-заголовков, которые выставлял бы middleware CORS,
    // если бы был возвращён в конвейер (источников CORS в стеке больше нет).
    private static readonly string[] CorsResponseHeaders =
    [
        "Access-Control-Allow-Origin",
        "Access-Control-Allow-Methods",
        "Access-Control-Allow-Headers",
        "Access-Control-Allow-Credentials",
        "Access-Control-Expose-Headers",
        "Access-Control-Max-Age",
    ];

    // Ответы на запросы с Origin (браузер так посылает кросс-доменные запросы):
    // middleware CORS выставлял бы Allow-Origin — конвейер не выставляет никогда.
    [Theory]
    [InlineData("/health")]
    [InlineData("/works")]
    [InlineData("/")]
    [InlineData("/api/v1/labs")]
    public async Task Responses_WithOriginHeader_CarryNoCorsHeaders(string path)
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Origin", "http://localhost:4200");

        var response = await client.SendAsync(request);

        foreach (var header in CorsResponseHeaders)
        {
            Assert.False(
                response.Headers.Contains(header),
                $"Ответ {path} содержит CORS-заголовок '{header}' — CORS настроен (запрещено FR-001).");
        }
    }

    // Preflight-OPTIONS не обслуживается CORS-ом: для пути с сопоставленным
    // GET-маршрутом маршрутизация отвечает фреймворковым 405 (метод не сопоставлен),
    // для полностью несопоставленного пути под /api — 404-конверт route-404-handler'а
    // (любой метод); в обоих случаях нет 204 и Allow-* заголовков CORS.
    [Theory]
    [InlineData("/api/v1/labs")]
    [InlineData("/api/v1/nothing")]
    public async Task Preflight_OptionsUnderApi_NotHandledByCors(string path)
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);
        if (path == "/api/v1/nothing")
        {
            // Полностью несопоставленный путь под /api — конверт любого метода.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(NotFoundEnvelope, await response.Content.ReadAsStringAsync());
        }

        foreach (var header in CorsResponseHeaders)
        {
            Assert.False(
                response.Headers.Contains(header),
                $"Preflight-ответ {path} содержит CORS-заголовок '{header}' — CORS настроен (запрещено FR-001).");
        }
    }

    // ------------------------------------------------------------------
    // Помощники.
    // ------------------------------------------------------------------

    private static IReadOnlyDictionary<string, string?> Settings(params (string Key, string? Value)[] items) =>
        items.ToDictionary(item => item.Key, item => item.Value);
}
