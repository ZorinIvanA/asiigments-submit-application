using System.Diagnostics;
using System.Globalization;
using LabsApp.IntegrationTests.B20.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B20.Scenarios;

/// <summary>
/// Кейс батча B-20: TS-178 «NFR-001: p95 списочных эндпойнтов ≤500 мс»
/// (NFR-001, P1).
/// given: Тестовый хост с данными: 200 работ, 300 пользователей, группа 25
///        студентов × семестр 20 работ (DI-сид B20DomainSeed — прямое
///        наполнение in-memory репозиториев, в обход регистрационного лимитера
///        FR-004); сессия teacher (минт access-JWT, ADR-015); Stopwatch
///        (WebApplicationFactory + Stopwatch, входит в dotnet test — ADR-016).
/// when:  100 последовательных GET /api/v1/labs; 100 × GET /api/v1/students;
///        100 × GET /api/v1/submissions?groupId=&lt;группа&gt;&amp;semester=&lt;семестр&gt;
///        (KDF-зависимые эндпойнты исключены — NFR-001).
/// then:  p95 времени ответа каждого маршрута ≤500 мс (NFR-001 constraint
///        дословно).
///
/// p95 — отсечка ceil(0.95·n) отсортированного массива Stopwatch-замеров
/// (n = 100 на маршрут). В статистику латентности попадают ТОЛЬКО успешные
/// (2xx) ответы: не-2xx — отдельное падение с диагностикой (предусловие
/// измеримости NFR-001), а не замер.
///
/// Тест включён в последовательную коллекцию зоны «b20-backend-dotnet-cli»:
/// соседние тесты зоны, запускающие дочерние dotnet/ng-процессы (TS-149 и
/// метатесты), конкурируют за CPU и obj/bin src/api — параллельность с ними
/// порождает шум латентности бенчмарка.
/// </summary>
[Collection("b20-backend-dotnet-cli")]
public sealed class Ts178_Nfr001ReadEndpointsP95Tests : IClassFixture<Ts178_Nfr001ReadEndpointsP95Tests.PerfFixture>
{
    private const int RequestsPerRoute = 100;
    private const double LatencyBudgetMs = 500.0;

    private readonly PerfFixture _fixture;

    public Ts178_Nfr001ReadEndpointsP95Tests(PerfFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Фикстура: хост + датасет 200 работ / 300 пользователей / группа 25 × семестр 20.</summary>
    public sealed class PerfFixture : IDisposable
    {
        public B20ApiFactory Factory { get; } = new();

        /// <summary>Данные given: целевая группа (25 студентов), целевой семестр (20 работ).</summary>
        public B20DomainSeed.ReadPerformanceDataset Dataset { get; }

        public PerfFixture()
        {
            Dataset = B20DomainSeed.SeedReadPerformanceDataset(Factory);

            // Предусловие given: объёмы датасета NFR-001 в хранилище хоста.
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
    public async Task HundredSequentialGets_OnEachListEndpoint_P95_IsAtMost500Ms()
    {
        // given: сессия teacher (KDF-зависимые эндпойнты не задействованы).
        var teacherSession = B20AuthSessions.CreateTeacherSession(_fixture.Factory);
        using var client = teacherSession.Client;
        var dataset = _fixture.Dataset;
        var routes = new[]
        {
            ("GET /api/v1/labs", "/api/v1/labs"),
            ("GET /api/v1/students", "/api/v1/students"),
            (
                "GET /api/v1/submissions?groupId=<группа>&semester=<семестр>",
                $"/api/v1/submissions?groupId={dataset.TargetGroupId}&semester={dataset.TargetSemester}"),
        };

        var latencies = new List<double>[routes.Length];
        var nonSuccess = new List<string>[routes.Length];
        for (var i = 0; i < routes.Length; i++)
        {
            latencies[i] = new List<double>(RequestsPerRoute);
            nonSuccess[i] = new List<string>();
        }

        // when: 100 ПОСЛЕДОВАТЕЛЬНЫХ GET каждого маршрута (Stopwatch на каждый).
        for (var routeIndex = 0; routeIndex < routes.Length; routeIndex++)
        {
            var path = routes[routeIndex].Item2;
            for (var i = 0; i < RequestsPerRoute; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                using var response = await client.GetAsync(path);
                stopwatch.Stop();

                if ((int)response.StatusCode is >= 200 and < 300)
                {
                    latencies[routeIndex].Add(stopwatch.Elapsed.TotalMilliseconds);
                }
                else
                {
                    nonSuccess[routeIndex].Add(
                        $"#{i + 1}: {(int)response.StatusCode} {response.StatusCode}");
                }
            }
        }

        // Предусловие then: все 300 запросов авторизованы и успешны — иначе
        // замеры латентности не являются предметом NFR-001.
        var statusFailures = routes
            .Select((route, index) => (route.Item1, nonSuccess[index]))
            .Where(item => item.Item2.Count > 0)
            .Select(item => $"{item.Item1}: {item.Item2.Count} не-2xx из {RequestsPerRoute} "
                + $"[{string.Join("; ", item.Item2.Take(5))}…]")
            .ToList();
        Assert.True(
            statusFailures.Count == 0,
            "Предусловие NFR-001 нарушено — списочные GET-маршруты не отдавали "
            + "успешные ответы (латентность таких ответов не предмет NFR-001): "
            + string.Join(" | ", statusFailures));

        // then: p95 времени ответа каждого маршрута ≤500 мс (NFR-001 дословно).
        var budgetViolations = new List<string>();
        for (var i = 0; i < routes.Length; i++)
        {
            var p95 = Percentile95(latencies[i]);
            if (p95 > LatencyBudgetMs)
            {
                budgetViolations.Add(
                    $"{routes[i].Item1}: p95 = {FormatMs(p95)} мс (бюджет — 500 мс), "
                    + $"медиана = {FormatMs(Percentile50(latencies[i]))} мс, "
                    + $"замеров = {latencies[i].Count}");
            }
        }

        Assert.True(
            budgetViolations.Count == 0,
            "NFR-001 нарушен (p95 ≤ 500 мс на каждом из списочных маршрутов): "
            + string.Join(" | ", budgetViolations));
    }

    /// <summary>p95: отсечка ceil(0.95·n) отсортированных замеров (n ≥ 1).</summary>
    private static double Percentile95(IReadOnlyList<double> samples)
    {
        Assert.True(samples.Count > 0, "Нет ни одного успешного замера латентности.");
        var sorted = samples.OrderBy(value => value).ToArray();
        var index = Math.Max(0, (int)Math.Ceiling(0.95 * sorted.Length) - 1);
        return sorted[index];
    }

    private static double Percentile50(IReadOnlyList<double> samples)
    {
        var sorted = samples.OrderBy(value => value).ToArray();
        return sorted[sorted.Length / 2];
    }

    private static string FormatMs(double value) =>
        value.ToString("F1", CultureInfo.InvariantCulture);
}
