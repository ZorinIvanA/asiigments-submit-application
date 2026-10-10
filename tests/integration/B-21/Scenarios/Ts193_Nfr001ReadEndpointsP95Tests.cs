using System.Diagnostics;
using LabsApp.IntegrationTests.B21.Infrastructure;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B21.Scenarios;

/// <summary>
/// TS-193 (NFR-001, P1): p95 ≤ 500 мс читающих эндпойнтов под нагрузкой.
///
/// given: интеграционный бенчмарк (WebApplicationFactory + Stopwatch);
///        предзаполнено 200 работ, 300 пользователей, ведомость группы
///        25 студентов × семестр 20 работ (DI-сид B21DomainSeed: объёмные данные
///        в обход HTTP-лимитов — регистрационный лимитер 5/час на IP, FR-004;
///        итог хранилища контролируется утверждениями фикстуры); сессия teacher
///        (минт access-JWT — ADR-015/ADR-022); KDF-зависимые эндпойнты
///        исключены (ни один из трёх путей не выполняет дериваций).
/// when:  по 100 последовательных запросов GET /api/v1/labs, GET /api/v1/students,
///        GET /api/v1/submissions (ведомость целевой группы за целевой семестр);
///        расчёт p95 каждого (Stopwatch на каждый запрос).
/// then:  p95 каждого эндпойнта ≤ 500 мс (NFR-001; методика verification —
///        «интеграционный бенчмарк-тест (WebApplicationFactory + Stopwatch),
///        включён в dotnet test», ADR-016).
///
/// p95 — отсечка ceil(0.95·n) отсортированного массива Stopwatch-замеров
/// (n = 100 на эндпойнт) — общий помощник зоны <see cref="B21LatencyStats"/>
/// (REWORK CR-002: один экземпляр помощника на зону вместо приватных копий).
/// Замеряются ТОЛЬКО успешные (2xx) ответы: латентность
/// ответа ошибки не является предметом NFR-001, поэтому не-2xx фиксируется
/// отдельным падением (с диагностикой), а не в статистику латентности.
/// </summary>
public sealed class Ts193_Nfr001ReadEndpointsP95Tests : IClassFixture<Ts193_Nfr001ReadEndpointsP95Tests.PerfFixture>
{
    private const int RequestsPerEndpoint = 100;
    private const double LatencyBudgetMs = 500.0;

    private readonly PerfFixture _fixture;

    public Ts193_Nfr001ReadEndpointsP95Tests(PerfFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Фикстура: хост + датасет 200 работ / 300 пользователей / группа 25 × семестр 20.</summary>
    public sealed class PerfFixture : IDisposable
    {
        public B21WebAppFactory Factory { get; } = new();

        /// <summary>Данные given: целевая группа (25 студентов), целевой семестр (20 работ).</summary>
        public B21DomainSeed.PerformanceDataset Dataset { get; }

        public PerfFixture()
        {
            Dataset = B21DomainSeed.SeedPerformanceDataset(Factory);

            // Итоговое состояние хранилища (given TS-193): 200 работ,
            // 300 пользователей (первые 25 — в целевой группе), ведомость
            // «группа 25 × семестр 20» заполнена записями сдач.
            Assert.Equal(200, Factory.Services.GetRequiredService<ILabRepository>().GetAll().Count);
            Assert.Equal(300, Factory.Services.GetRequiredService<IUserRepository>().ListStudents().Count);
            Assert.Equal(
                25,
                Factory.Services.GetRequiredService<IUserRepository>()
                    .ListStudents().Count(student => student.GroupId == Dataset.TargetGroupId));
            var groupStudentIds = Factory.Services.GetRequiredService<IUserRepository>()
                .ListByGroup(Dataset.TargetGroupId)
                .Select(student => student.Id)
                .ToArray();
            Assert.Equal(25, groupStudentIds.Length);
            Assert.Equal(
                Dataset.SubmittedPairCount,
                Factory.Services.GetRequiredService<ISubmissionRepository>()
                    .GetByPairs(groupStudentIds, Dataset.TargetSemesterLabIds).Count);
        }

        public void Dispose() => Factory.Dispose();
    }

    [Fact]
    public async Task HundredSequentialGets_OnEachOfThreeEndpoints_P95_IsAtMost500Ms()
    {
        // given: сессия teacher (минт access-cookie; KDF-зависимые эндпойнты
        // не задействованы); клиент освобождается вместе с областью теста (CR-004).
        var teacherSession = B21Sessions.CreateTeacherSession(_fixture.Factory);
        using var client = teacherSession.Client;
        var dataset = _fixture.Dataset;
        var endpoints = new[]
        {
            ("GET /api/v1/labs", "/api/v1/labs"),
            ("GET /api/v1/students", "/api/v1/students"),
            (
                "GET /api/v1/submissions",
                $"/api/v1/submissions?groupId={dataset.TargetGroupId}&semester={dataset.TargetSemester}"),
        };

        // Предусловие given: ведомость группы 25×20 реально наполнена —
        // ответ GET /api/v1/submissions содержит ненулевой массив submissions
        // для пар текущей страницы студентов (с пустой ведомостью замер p95
        // третьего эндпойнта не имел бы предмета).
        using (var gridProbe = await client.GetAsync(endpoints[2].Item2))
        {
            Assert.True(
                (int)gridProbe.StatusCode is >= 200 and < 300,
                $"Предусловие NFR-001: пробный GET ведомости ответил "
                + $"{(int)gridProbe.StatusCode} {gridProbe.StatusCode}.");
            using var document = JsonDocument.Parse(await gridProbe.Content.ReadAsStringAsync());
            var submissionsArray = document.RootElement.GetProperty("submissions");
            Assert.True(
                submissionsArray.ValueKind == JsonValueKind.Array
                && submissionsArray.GetArrayLength() > 0,
                "Предусловие NFR-001: в ответе GET /api/v1/submissions ПУСТ массив "
                + "submissions — ведомость группы 25×20 не наполнена датасетом given, "
                + "замер p95 третьего эндпойнта не имел бы предмета.");
        }

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

        // Предусловие: все 300 запросов авторизованы и успешны — иначе замеры
        // латентности не являются предметом NFR-001.
        var statusFailures = endpoints
            .Select((endpoint, index) => (endpoint.Item1, nonSuccess[index]))
            .Where(item => item.Item2.Count > 0)
            .Select(item => $"{item.Item1}: {item.Item2.Count} не-2xx из {RequestsPerEndpoint} "
                + $"[{string.Join("; ", item.Item2.Take(5))}…]")
            .ToList();
        Assert.True(
            statusFailures.Count == 0,
            "Предусловие NFR-001 нарушено — читающие GET-эндпойнты не отдавали успешные "
            + "ответы (латентность таких ответов не предмет NFR-001): "
            + string.Join(" | ", statusFailures));

        // then: p95 каждого эндпойнта ≤ 500 мс.
        var budgetViolations = new List<string>();
        for (var i = 0; i < endpoints.Length; i++)
        {
            var p95 = B21LatencyStats.Percentile95(latencies[i]);
            if (p95 > LatencyBudgetMs)
            {
                budgetViolations.Add(
                    $"{endpoints[i].Item1}: p95 = {B21LatencyStats.FormatMs(p95)} мс (бюджет — 500 мс), "
                    + $"медиана = {B21LatencyStats.FormatMs(B21LatencyStats.Percentile50(latencies[i]))} мс, "
                    + $"замеров = {latencies[i].Count}");
            }
        }

        Assert.True(
            budgetViolations.Count == 0,
            "NFR-001 нарушен (p95 ≤ 500 мс на каждом из трёх читающих эндпойнтов): "
            + string.Join(" | ", budgetViolations));
    }
}
