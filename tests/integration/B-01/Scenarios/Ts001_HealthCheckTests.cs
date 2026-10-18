using LabsApp.IntegrationTests.B01.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-001 «Health-check: 200 {'status':'ok'} без аутентификации, &lt;100 мс»
/// (FR-001, P0).
/// given: тестовый хост ApiFactory (среда Development) запущен;
/// аутентификационных cookie в запросе нет.
/// when: GET /health.
/// then: 200; тело JSON {'status':'ok'}; время ответа &lt;100 мс.
/// </summary>
public sealed class Ts001_HealthCheckTests
{
    /// <summary>Граница времени ответа health-check (FR-001, AC «Health-check»).</summary>
    private static readonly TimeSpan ResponseDeadline = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task GetHealth_InDevelopment_ReturnsOkUnder100MillisecondsWithoutCookie()
    {
        // given: Development-стенд; запрос выполняется клиентом без cookie.
        using var factory = new B01WebAppFactory(Environments.Development, null);
        using var client = HostClients.Create(factory);

        // Прогрев соединения/JIT: замеряется устойчивое время обработки /health,
        // а не холодный старт тестового хоста.
        using (var warmup = await client.GetAsync("/health"))
        {
            Assert.Equal(HttpStatusCode.OK, warmup.StatusCode);
        }

        // when: GET /health без cookie с замером времени.
        var stopwatch = Stopwatch.StartNew();
        using var response = await client.GetAsync("/health");
        stopwatch.Stop();

        // then: 200; тело {"status":"ok"}; время ответа < 100 мс.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync());
        Assert.True(
            stopwatch.Elapsed < ResponseDeadline,
            $"Ожидалось время ответа < {ResponseDeadline.TotalMilliseconds} мс, фактически: {stopwatch.Elapsed.TotalMilliseconds:F1} мс.");
    }
}
