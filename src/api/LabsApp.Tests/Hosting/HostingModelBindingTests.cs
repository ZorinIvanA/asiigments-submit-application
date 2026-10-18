using System.Net;
using System.Text;
using LabsApp.Hosting;

namespace LabsApp.Tests.Hosting;

/// <summary>
/// Интеграционные проверки глобального camelCase-JSON (FR-001) и нормализации
/// фреймворковых 400 в ErrorEnvelope без errors (IF-001) — на служебном
/// контроллере привязки, подключаемом только в тестовом хосте.
/// </summary>
public sealed class HostingModelBindingTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Bind_ValidJson_ReturnsCamelCaseJson()
    {
        var response = await _client.PostAsync(
            "/api/v1/host-smoke/bind",
            new StringContent("{\"page\":7}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"page\":7}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Bind_MalformedJson_ReturnsErrorEnvelopeWithoutErrors()
    {
        var response = await _client.PostAsync(
            "/api/v1/host-smoke/bind",
            new StringContent("{not-json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"{{\"message\":\"{ErrorTexts.InvalidData}\"}}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Bind_MissingBody_ReturnsErrorEnvelopeWithoutErrors()
    {
        var response = await _client.PostAsync(
            "/api/v1/host-smoke/bind",
            new StringContent(string.Empty, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal($"{{\"message\":\"{ErrorTexts.InvalidData}\"}}", body);
        Assert.DoesNotContain("errors", body, StringComparison.Ordinal);
    }

    // IF-001: фреймворковая ошибка привязки (несовместимый тип поля) — тот же
    // конверт без errors; errors появляется только у полевой валидации.
    [Fact]
    public async Task Bind_TypeMismatch_ReturnsErrorEnvelopeWithoutErrors()
    {
        var response = await _client.PostAsync(
            "/api/v1/host-smoke/bind",
            new StringContent("{\"page\":\"not-a-number\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal($"{{\"message\":\"{ErrorTexts.InvalidData}\"}}", body);
        Assert.DoesNotContain("errors", body, StringComparison.Ordinal);
    }
}
