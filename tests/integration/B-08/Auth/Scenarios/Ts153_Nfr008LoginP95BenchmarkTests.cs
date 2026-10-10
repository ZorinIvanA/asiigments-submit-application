using System.Diagnostics;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Auth.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Auth.Scenarios;

/// <summary>
/// TS-153 «NFR-008: p95 login ≤300 мс при тестовых итерациях KDF»
/// (nfr, NFR-008 + FR-005, P1).
///
/// given: Auth__Pbkdf2Iterations=1000; Stopwatch; существующий пользователь с
///        известным паролем.
/// when:  100 последовательных POST /auth/login (чередование верного и
///        неверного пароля).
/// then:  p95 ≤300 мс — отсутствие лишних дериваций и накладных расходов
///        (NFR-008, методика verification: бенчмарк-тест с закреплённой
///        конфигурацией).
///
/// p95 — статистика ближайшего ранга: 95-й элемент отсортированного вектора
/// из 100 длительностей. Неудачные попытки после пятой метки лимитера отвечают
/// 429, но по контракту FR-007 и в 401-, и в 429-ветке выполняется РОВНО одна
/// деривация (ShouldBlock — после KDF), поэтому чередование не искажает
/// измерение стоимости.
/// </summary>
public sealed class Ts153_Nfr008LoginP95BenchmarkTests
{
    private const string Login = "p95bench";
    private const string Email = "p95bench@example.com";
    private const string WrongPassword = "Wrong0rd!";
    private const int RequestCount = 100;
    private const double P95ThresholdMilliseconds = 300;

    [Fact]
    public async Task Login_100SequentialRequests_P95Within300Milliseconds()
    {
        using var factory = new B08AuthDevFactory();
        using var client = B08AuthHost.CreateClient(factory);

        // given: Auth__Pbkdf2Iterations=1000 (фикстура); существующий
        // пользователь с известным паролем (DI-сид); Stopwatch.
        B08AuthHost.SeedUser(factory, Login, Email, "Бенчмарк Вход", UserRoles.Student);
        var correctBody = "{\"login\":\"" + Login + "\",\"password\":\"" + B08AuthHost.TestUserPassword + "\"}";
        var wrongBody = "{\"login\":\"" + Login + "\",\"password\":\"" + WrongPassword + "\"}";

        // when: 100 последовательных POST /auth/login с чередованием верного и
        // неверного пароля; длительность каждого — по Stopwatch.
        var durationsMilliseconds = new List<double>(RequestCount);
        var stopwatch = new Stopwatch();
        for (var index = 0; index < RequestCount; index++)
        {
            var body = index % 2 == 0 ? correctBody : wrongBody;
            stopwatch.Restart();
            using var response = await client.PostAsync(B08AuthHost.LoginEndpoint, body);
            stopwatch.Stop();
            durationsMilliseconds.Add(stopwatch.Elapsed.TotalMilliseconds);

            Assert.True(
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests,
                $"запрос {index}: неожиданный статус {(int)response.StatusCode} (ожидались только 200/401/429).");
        }

        // then: p95 ≤300 мс (ближайший ранг: 95-й элемент сортировки).
        durationsMilliseconds.Sort();
        var p95 = durationsMilliseconds[95 - 1];
        Assert.True(
            p95 <= P95ThresholdMilliseconds,
            $"p95 входа {p95:F1} мс превысил порог {P95ThresholdMilliseconds:F0} мс " +
            $"(мин {durationsMilliseconds[0]:F1}, медиана {durationsMilliseconds[49]:F1}, макс {durationsMilliseconds[^1]:F1}).");
    }
}
