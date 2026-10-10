using LabsApp.IntegrationTests.B01.Infrastructure;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-012 «Защитные заголовки на статике и fallback (SEC-006)» (FR-002, P0).
/// given: wwwroot/index.html и wwwroot/favicon.ico существуют.
/// when: GET /works, GET / (корень), GET /favicon.ico.
/// then: каждый ответ содержит заголовки X-Content-Type-Options: nosniff и
/// X-Frame-Options: DENY (полная CSP — out_of_scope, её отсутствие — кейс TS-013).
/// (В предыдущей нумерации зоны кейс значился как TS-011 — файл переименован
/// по актуальному набору кейсов батча.)
/// </summary>
public sealed class Ts012_SecurityHeadersTests
{
    internal static async Task AssertSecurityHeadersAsync(HttpClient client, string path)
    {
        // when: GET статического маршрута (fallback либо default-файл).
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // then: X-Content-Type-Options: nosniff.
        Assert.True(
            response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff)
            && nosniff.Contains("nosniff", StringComparer.OrdinalIgnoreCase),
            $"{path}: ожидался заголовок X-Content-Type-Options: nosniff, фактически: "
            + (response.Headers.Contains("X-Content-Type-Options")
                ? string.Join(", ", response.Headers.GetValues("X-Content-Type-Options"))
                : "заголовок отсутствует"));

        // then: X-Frame-Options: DENY.
        Assert.True(
            response.Headers.TryGetValues("X-Frame-Options", out var frameOptions)
            && frameOptions.Contains("DENY", StringComparer.OrdinalIgnoreCase),
            $"{path}: ожидался заголовок X-Frame-Options: DENY, фактически: "
            + (response.Headers.Contains("X-Frame-Options")
                ? string.Join(", ", response.Headers.GetValues("X-Frame-Options"))
                : "заголовок отсутствует"));
    }

    [Fact]
    public async Task GetWorks_FallbackCarriesSecurityHeaders()
    {
        // given: wwwroot/index.html существует.
        using var factory = new B01WebAppFactory();
        using var client = HostClients.Create(factory);

        // when/then: deep-link обслуживает fallback.
        await AssertSecurityHeadersAsync(client, "/works");
    }

    [Fact]
    public async Task GetRoot_DefaultFileCarriesSecurityHeaders()
    {
        // given: wwwroot/index.html существует (корень отдаёт default-файл статики).
        using var factory = new B01WebAppFactory();
        using var client = HostClients.Create(factory);

        // when/then: корень обслуживает статику.
        await AssertSecurityHeadersAsync(client, "/");
    }

    [Fact]
    public async Task GetFaviconIco_StaticFileCarriesSecurityHeaders()
    {
        // given: wwwroot/favicon.ico существует (TestAssets копируется csproj-целью).
        using var factory = new B01WebAppFactory();
        using var client = HostClients.Create(factory);

        // when/then: существующий файл статики отдаётся напрямую с заголовками.
        await AssertSecurityHeadersAsync(client, "/favicon.ico");
    }
}
