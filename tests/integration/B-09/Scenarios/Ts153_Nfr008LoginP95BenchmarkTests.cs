using System.Diagnostics;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-153 «NFR-008: p95 login ≤300 мс при тестовых итерациях KDF» (nfr,
/// NFR-008 + FR-005, P1).
///
/// given: Auth__Pbkdf2Iterations=1000 (фикстура); Stopwatch; существующий
///        пользователь с известным паролем.
/// when:  100 последовательных POST /auth/login (чередование верного и
///        неверного пароля).
/// then:  p95 ≤300 мс — отсутствие лишних дериваций и накладных расходов
///        (NFR-008, методика verification: бенчмарк-тест с закреплённой
///        конфигурацией).
///
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-08/Auth (староволновой Ts193_LoginBenchmark в B-21/B-22
/// помечен арбитражем дубликатом). Поведенческая часть кейса исполнима дословно
/// и исполнена в собственной зоне батча B-09 (прецедент c-1052); расхождение
/// размещения зафиксировано в scenario_change_requests.
///
/// p95 — статистика ближайшего ранга: 95-й элемент отсортированного вектора
/// из 100 длительностей. Неудачные попытки после пятой метки лимитера отвечают
/// 429, но по контракту FR-007 и в 401-, и в 429-ветке выполняется РОВНО одна
/// деривация (ShouldBlock — после KDF), поэтому чередование не искажает
/// измерение стоимости.
/// </summary>
public sealed class Ts153_Nfr008LoginP95BenchmarkTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string Login = "p95bench09";
    private const string Email = "p95bench09@example.com";
    private const string RegistrationIp = "10.0.0.153";
    private const string ClientIp = "10.0.0.154";
    private const string WrongPassword = "Wrong0rd!";
    private const int RequestCount = 100;
    private const double P95ThresholdMilliseconds = 300;

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task Login_100SequentialRequests_P95Within300Milliseconds()
    {
        // given: Auth__Pbkdf2Iterations=1000 (фикстура); существующий пользователь
        // с известным паролем (регистрация через публичный API — 1 KDF register);
        // Stopwatch.
        await B09HostClients.RegisterStudentAsync(
            _factory, "Бенчмарк Девяносто Три", Login, Email, ip: RegistrationIp);

        using var client = B09AuthHttp.Create(_factory, ClientIp);
        var stopwatch = Stopwatch.StartNew();
        var durations = new List<double>(RequestCount);

        // when: 100 последовательных POST /auth/login (чередование верного и
        // неверного пароля).
        for (var attempt = 0; attempt < RequestCount; attempt++)
        {
            var correctPassword = attempt % 2 == 0;
            stopwatch.Reset();
            using var response = await B09AuthHttp.LoginAsync(
                client, Login, correctPassword ? B09HostClients.TestUserPassword : WrongPassword);
            stopwatch.Stop();

            Assert.True(
                response.StatusCode is HttpStatusCode.OK
                    or HttpStatusCode.Unauthorized
                    or HttpStatusCode.TooManyRequests,
                $"Бенчмарк TS-153: неожиданный статус {(int)response.StatusCode} на попытке {attempt + 1} " +
                "(ожидались только ветки 200/401/429 — каждая с ровно одной деривацией, FR-007).");
            durations.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        // then: p95 ≤300 мс (95-й элемент отсортированного вектора —
        // статистика ближайшего ранга).
        durations.Sort();
        var p95 = durations[95 - 1];
        var max = durations[^1];
        Assert.True(
            p95 <= P95ThresholdMilliseconds,
            $"NFR-008: p95 длительности login = {p95:F1} мс превышает порог " +
            $"{P95ThresholdMilliseconds:F0} мс на {RequestCount} последовательных запросов " +
            $"(max: {max:F1} мс; проверь отсутствие лишних дериваций/накладных расходов).");
    }
}
