using LabsApp.IntegrationTests.B02.Infrastructure;

namespace LabsApp.IntegrationTests.B02.Scenarios;

/// <summary>
/// TS-158 «Scope: полная Content-Security-Policy не добавляется» (FR-002/SEC-006,
/// out_of_scope: полная CSP, P2).
///
/// given: wwwroot/index.html существует (тестовый wwwroot зоны копируется csproj-целью
///        в выходной каталог — content root фабрики); хост запущен.
/// when:  GET /works; GET /; GET /favicon.ico; инспекция заголовков ответов.
/// then:  заголовок Content-Security-Policy отсутствует (как и вариант
///        Content-Security-Policy-Report-Only); из защитных заголовков присутствуют
///        ТОЛЬКО X-Content-Type-Options: nosniff и X-Frame-Options: DENY — иных
///        защитных заголовков ответ не содержит.
/// </summary>
public sealed class Ts158_ContentSecurityPolicyScopeTests : IClassFixture<B02WebAppFactory>
{
    /// <summary>
    /// Известные защитные заголовки, запрещённые на статических маршрутах: варианты
    /// CSP (полная CSP — out_of_scope) и иные защитные заголовки сверх оговорённых
    /// nosniff/DENY — уточнение «только» из then кейса.
    /// </summary>
    private static readonly string[] ForbiddenSecurityHeaders =
    {
        "Content-Security-Policy",
        "Content-Security-Policy-Report-Only",
        "Cross-Origin-Embedder-Policy",
        "Cross-Origin-Opener-Policy",
        "Cross-Origin-Resource-Policy",
        "Permissions-Policy",
        "Referrer-Policy",
        "Strict-Transport-Security",
        "X-Permitted-Cross-Domain-Policies",
        "X-Powered-By",
        "X-XSS-Protection",
    };

    private readonly B02WebAppFactory _factory;

    public Ts158_ContentSecurityPolicyScopeTests(B02WebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/works")]
    [InlineData("/")]
    [InlineData("/favicon.ico")]
    public async Task StaticRoutes_NoContentSecurityPolicy_ProtectiveHeadersOnly(string path)
    {
        // given: wwwroot/index.html существует в content root тестового хоста
        // (предусловие кейса фиксируется явной диагностикой).
        var indexHtml = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        Assert.True(
            File.Exists(indexHtml),
            $"Предусловие кейса нарушено: {indexHtml} не существует (csproj-цель CopyTestWwwroot).");

        using var client = _factory.CreateWarmClient();

        // when: GET статического маршрута (SPA fallback, default-файл или файл напрямую).
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // then: заголовок Content-Security-Policy отсутствует, как и его вариант
        // Content-Security-Policy-Report-Only (полная CSP — out_of_scope).
        Assert.False(
            response.Headers.Contains("Content-Security-Policy")
            || response.Headers.Contains("Content-Security-Policy-Report-Only"),
            $"{path}: обнаружен заголовок Content-Security-Policy[-Report-Only] — полная CSP "
            + "не входит в область (out_of_scope), её добавление не требуется и не допускается "
            + "контрактом FR-002.");

        // then: иных защитных заголовков (кроме оговорённых nosniff и DENY) ответ
        // не содержит — «присутствуют только ...» из then кейса (заголовки смотрятся
        // и в основных, и в заголовках содержимого ответа).
        var unexpectedSecurityHeaders = response.Headers
            .Concat(response.Content.Headers)
            .Select(header => header.Key)
            .Where(name => ForbiddenSecurityHeaders.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        Assert.True(
            unexpectedSecurityHeaders.Count == 0,
            $"{path}: обнаружены защитные заголовки вне оговорённого состава "
            + $"({string.Join(", ", unexpectedSecurityHeaders)}) — из защитных заголовков "
            + "контрактом FR-002/SEC-006 оговорены только X-Content-Type-Options: nosniff "
            + "и X-Frame-Options: DENY.");

        // then: из защитных заголовков присутствуют ровно оговорённые:
        // X-Content-Type-Options: nosniff и X-Frame-Options: DENY (SEC-006).
        Assert.True(
            response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff)
            && nosniff.Contains("nosniff", StringComparer.OrdinalIgnoreCase),
            $"{path}: ожидался заголовок X-Content-Type-Options: nosniff, фактически: "
            + (response.Headers.Contains("X-Content-Type-Options")
                ? string.Join(", ", response.Headers.GetValues("X-Content-Type-Options"))
                : "заголовок отсутствует"));
        Assert.True(
            response.Headers.TryGetValues("X-Frame-Options", out var frameOptions)
            && frameOptions.Contains("DENY", StringComparer.OrdinalIgnoreCase),
            $"{path}: ожидался заголовок X-Frame-Options: DENY, фактически: "
            + (response.Headers.Contains("X-Frame-Options")
                ? string.Join(", ", response.Headers.GetValues("X-Frame-Options"))
                : "заголовок отсутствует"));
    }
}
