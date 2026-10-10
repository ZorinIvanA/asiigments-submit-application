using System.Diagnostics;
using System.Globalization;
using LabsApp.IntegrationTests.B22.Infrastructure;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B22.Scenarios;

/// <summary>
/// TS-184 (NFR-001, P1): p95 ≤ 500 мс на трёх GET-эндпойнтах.
///
/// given: интеграционный стенд (WebApplicationFactory + Stopwatch); объёмные
///        данные готовятся в обход HTTP-лимитов — напрямую через шов
///        репозиториев (DI-сид B22DomainSeed; создание через POST /auth/register
///        недопустимо: регистрационный лимитер 5/час на IP, FR-004); итоговое
///        состояние — 200 работ, 300 пользователей, группа 25 студентов ×
///        семестр 20 работ; KDF-зависимые эндпойнты не задействованы.
/// when:  100 последовательных запросов каждого: GET /api/v1/labs,
///        GET /api/v1/students, GET /api/v1/submissions?groupId&semester
///        (авторизованные, сессия teacher — минт access-JWT, ADR-015).
/// then:  p95 времени ответа каждого эндпойнта ≤ 500 мс (NFR-001; метод
///        verification — бенчмарк-тест в dotnet test, ADR-016).
///
/// p95 — отсечка ceil(0.95·n) отсортированного массива Stopwatch-замеров
/// (n = 100 на эндпойнт). Замеряются ТОЛЬКО успешные (2xx) ответы: ответ ошибки
/// не является предметом NFR-001 («GET /api/v1/labs при 200 работах»), поэтому
/// не-2xx фиксируется отдельным падением (с диагностикой), а не в статистику
/// латентности.
/// </summary>
public sealed class Ts184_PerformanceBenchmarksTests : IClassFixture<Ts184_PerformanceBenchmarksTests.PerfFixture>
{
    private const int RequestsPerEndpoint = 100;
    private const double LatencyBudgetMs = 500.0;

    private readonly PerfFixture _fixture;

    public Ts184_PerformanceBenchmarksTests(PerfFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Фикстура: хост + датасет 200 работ / 300 пользователей / группа 25 × семестр 20.</summary>
    public sealed class PerfFixture : IDisposable
    {
        public B22WebAppFactory Factory { get; } = new();

        /// <summary>Данные given: целевая группа (25 студентов), целевой семестр (20 работ).</summary>
        public B22DomainSeed.PerformanceDataset Dataset { get; }

        public PerfFixture()
        {
            Dataset = B22DomainSeed.SeedPerformanceDataset(Factory);

            Assert.Equal(200, Factory.Services.GetRequiredService<ILabRepository>().GetAll().Count);
            Assert.Equal(300, Factory.Services.GetRequiredService<IUserRepository>().ListStudents().Count);
            Assert.Equal(
                25,
                Factory.Services.GetRequiredService<IUserRepository>()
                    .ListStudents().Count(student => student.GroupId == Dataset.TargetGroupId));
        }

        public void Dispose() => Factory.Dispose();
    }

    [Fact]
    public async Task HundredSequentialGets_OnEachOfThreeEndpoints_P95_IsAtMost500Ms()
    {
        // given: авторизованные запросы — сессия teacher (минт access-cookie).
        var teacherSession = B22Sessions.CreateTeacherSession(_fixture.Factory);
        using var client = teacherSession.Client;
        var dataset = _fixture.Dataset;
        var endpoints = new[]
        {
            ("GET /api/v1/labs", "/api/v1/labs"),
            ("GET /api/v1/students", "/api/v1/students"),
            (
                "GET /api/v1/submissions?groupId&semester",
                $"/api/v1/submissions?groupId={dataset.TargetGroupId}&semester={dataset.TargetSemester}"),
        };

        var latencies = new List<double>[endpoints.Length];
        var nonSuccess = new List<string>[endpoints.Length];
        for (var i = 0; i < endpoints.Length; i++)
        {
            latencies[i] = new List<double>(RequestsPerEndpoint);
            nonSuccess[i] = new List<string>();
        }

        // when: 100 ПОСЛЕДОВАТЕЛЬНЫХ запросов каждого эндпойнта (Stopwatch на каждый).
        for (var endpointIndex = 0; endpointIndex < endpoints.Length; endpointIndex++)
        {
            var path = endpoints[endpointIndex].Item2;
            for (var i = 0; i < RequestsPerEndpoint; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                using var response = await client.GetAsync(path);
                stopwatch.Stop();

                if ((int)response.StatusCode is >= 200 and < 300)
                {
                    latencies[endpointIndex].Add(stopwatch.Elapsed.TotalMilliseconds);
                }
                else
                {
                    nonSuccess[endpointIndex].Add(
                        $"#{i + 1}: {(int)response.StatusCode} {response.StatusCode}");
                }
            }
        }

        // Предусловие then: все 300 запросов авторизованы и успешны — иначе замеры
        // латентности не являются предметом NFR-001.
        var statusFailures = endpoints
            .Select((endpoint, index) => (endpoint.Item1, nonSuccess[index]))
            .Where(item => item.Item2.Count > 0)
            .Select(item => $"{item.Item1}: {item.Item2.Count} не-2xx из {RequestsPerEndpoint} "
                + $"[{string.Join("; ", item.Item2.Take(5))}…]")
            .ToList();
        Assert.True(
            statusFailures.Count == 0,
            "Предусловие NFR-001 нарушено — GET-эндпойнты не отдавали успешные ответы "
            + "(латентность таких ответов не предмет NFR-001): " + string.Join(" | ", statusFailures));

        // then: p95 каждого эндпойнта ≤ 500 мс.
        var budgetViolations = new List<string>();
        for (var i = 0; i < endpoints.Length; i++)
        {
            var p95 = Percentile(latencies[i], 0.95);
            if (p95 > LatencyBudgetMs)
            {
                budgetViolations.Add(
                    $"{endpoints[i].Item1}: p95 = {FormatMs(p95)} мс (бюджет — 500 мс), "
                    + $"медиана = {FormatMs(Percentile(latencies[i], 0.5))} мс, замеров = {latencies[i].Count}");
            }
        }

        Assert.True(
            budgetViolations.Count == 0,
            "NFR-001 нарушен (p95 ≤ 500 мс на каждом из трёх GET-эндпойнтов): "
            + string.Join(" | ", budgetViolations));
    }

    /// <summary>Персентиль: отсечка ceil(p·n) отсортированных замеров (n ≥ 1).</summary>
    private static double Percentile(IReadOnlyList<double> samples, double portion)
    {
        Assert.True(samples.Count > 0, "Нет ни одного успешного замера латентности.");
        var sorted = samples.OrderBy(value => value).ToArray();
        var index = Math.Max(0, (int)Math.Ceiling(portion * sorted.Length) - 1);
        return sorted[index];
    }

    private static string FormatMs(double value) =>
        value.ToString("F1", CultureInfo.InvariantCulture);
}
