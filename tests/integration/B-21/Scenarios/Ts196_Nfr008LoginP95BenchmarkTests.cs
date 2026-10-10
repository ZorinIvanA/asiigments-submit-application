using System.Diagnostics;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B21.Infrastructure;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B21.Scenarios;

/// <summary>
/// TS-196 (NFR-008, P1): p95 входа ≤ 300 мс при тестовых итерациях KDF.
///
/// given: Auth__Pbkdf2Iterations=1000 (B21WebAppFactory.LoginBenchmark —
///        UseSetting на тестовом хосте зоны B-21); пользователь teacher
///        существует (сид FR-025, хэш пароля — реальным IPasswordHasher стенда
///        с параметрами KDF его конфигурации; ADR-007: Verify читает параметры
///        из хэша); бенчмарк в dotnet test (WebApplicationFactory + Stopwatch,
///        ADR-016).
/// when:  100 последовательных POST /api/v1/auth/login (успешных, верные
///        учётные данные сида-преподавателя); расчёт p95 (Stopwatch на каждый;
///        login-лимитер учитывает только НЕудачи — FR-004).
/// then:  p95 ≤ 300 мс (отсутствие лишних дериваций; NFR-008; verification —
///        «бенчмарк-тест в dotnet test с закреплённой конфигурацией»).
///
/// Предусловие given фиксируется утверждением: эффективное число итераций KDF
/// читается из хранимого хэша учётки teacher (формат FR-005:
/// 'pbkdf2-sha256$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;').
/// Без этой фиксации бенчмарк прошёл бы вакуумно: если
/// UseSetting("Auth:Pbkdf2Iterations","1000") не применился, login деривирует
/// по умолчанию (~210000) и всё равно укладывается в бюджет 300 мс на быстром
/// CPU, теряя предмет проверки «отсутствие лишних дериваций при тестовых
/// параметрах KDF». Замеряются ТОЛЬКО успешные (2xx) ответы логина: не-2xx
/// фиксируется отдельным падением (с диагностикой), а не в статистику.
///
/// Статус файла (REWORK CR-001/CR-002 батча B-21): единственный держатель
/// login-бенчмарка NFR-008 зоны — староволновой дубль
/// Ts193_LoginBenchmarkTests (устаревший ID «TS-193/NFR-008» доволновой
/// нумерации, тело бенчмарка дословно) СЛИТ с этим классом и удалён:
/// один класс на сценарный ID — TS-196, без второго 100-логинного
/// бенчмарка. Помощники (разбор итераций из хэша, p95/медиана) — общие,
/// <see cref="B21StoredPasswordHash"/> и <see cref="B21LatencyStats"/>.
/// </summary>
public sealed class Ts196_Nfr008LoginP95BenchmarkTests : IClassFixture<Ts196_Nfr008LoginP95BenchmarkTests.Fixture>
{
    private const int Requests = 100;
    private const double LatencyBudgetMs = 300.0;

    /// <summary>Тестовые параметры KDF кейса (given TS-196: Auth__Pbkdf2Iterations=1000).</summary>
    private const int TestKdfIterations = 1000;

    private readonly Fixture _fixture;

    public Ts196_Nfr008LoginP95BenchmarkTests(Fixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Фикстура: стенд Auth__Pbkdf2Iterations=1000 (учётка teacher — сид хоста).</summary>
    public sealed class Fixture : IDisposable
    {
        public B21WebAppFactory.LoginBenchmark Factory { get; } = new();

        public void Dispose() => Factory.Dispose();
    }

    [Fact]
    public async Task HundredSequentialSuccessfulLogins_AtTestKdfIterations_P95_IsAtMost300Ms()
    {
        // given: пользователь teacher существует (сид FR-025);
        // Auth__Pbkdf2Iterations=1000 — предусловие фиксируется по хэшу teacher.
        var storedUser = _fixture.Factory.Services.GetRequiredService<IUserRepository>()
            .GetByLogin(SeedOptions.DefaultTeacherLogin);
        Assert.True(
            storedUser is not null,
            "Предусловие given: пользователь teacher не найден в DI-хранилище тестового хоста.");
        var effectiveIterations = B21StoredPasswordHash.Iterations(storedUser!.PasswordHash);
        Assert.True(
            effectiveIterations == TestKdfIterations,
            $"Предусловие given TS-196 не исполнимо: стенд работает не при {TestKdfIterations} "
            + "итерациях KDF — UseSetting(\"Auth:Pbkdf2Iterations\",\"1000\") не отразился на "
            + $"хранимом хэше teacher (эффективно: {effectiveIterations} итераций, хэш: "
            + $"'{storedUser.PasswordHash}'). NFR-008 теряет предмет проверки «отсутствие "
            + "лишних дериваций при тестовых параметрах KDF»: бенчмарк при итерациях по "
            + "умолчанию прошёл бы вакуумно.");

        using var client = B21Sessions.Create(_fixture.Factory);
        var latencies = new List<double>(Requests);
        var failures = new List<string>();

        // when: 100 последовательных POST /auth/login (успешных — верные
        // учётные данные сида-преподавателя).
        for (var i = 0; i < Requests; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
            {
                login = SeedOptions.DefaultTeacherLogin,
                password = B21WebAppFactory.TestTeacherPassword,
            });
            stopwatch.Stop();

            if ((int)response.StatusCode is >= 200 and < 300)
            {
                latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
            else
            {
                failures.Add(
                    $"#{i + 1}: {(int)response.StatusCode} {response.StatusCode}, "
                    + $"тело: {(await response.Content.ReadAsStringAsync()).Trim()}");
            }
        }

        // Предусловие then: все 100 логинов успешны — иначе замеры не являются
        // предметом NFR-008 («100 последовательных POST /auth/login (успешных)»).
        Assert.True(
            failures.Count == 0,
            "Предусловие NFR-008 нарушено — POST /api/v1/auth/login с верными учётными "
            + $"данными teacher не успешен ({failures.Count} из {Requests}): "
            + string.Join(" | ", failures.Take(5)));

        // then: p95 ≤ 300 мс (отсутствие лишних дериваций).
        var p95 = B21LatencyStats.Percentile95(latencies);
        Assert.True(
            p95 <= LatencyBudgetMs,
            $"NFR-008: p95 POST /api/v1/auth/login = "
            + $"{B21LatencyStats.FormatMs(p95)} мс (бюджет — 300 мс), "
            + $"медиана = {B21LatencyStats.FormatMs(B21LatencyStats.Percentile50(latencies))} мс, "
            + $"замеров = {latencies.Count}.");
    }
}
