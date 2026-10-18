using System.Diagnostics;
using System.Globalization;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B22.Infrastructure;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B22.Scenarios;

/// <summary>
/// TS-193 (NFR-008, P1): p95 login ≤ 300 мс при тестовых итерациях KDF.
///
/// given: стенд с Auth__Pbkdf2Iterations=1000 (B22WebAppFactory.LoginBenchmark);
///        пользователь сида существует (DI-сид через IUserRepository; хэш пароля —
///        реальным IPasswordHasher стенда, т.е. с параметрами KDF его конфигурации,
///        ADR-007: Verify читает параметры из хэша; метка seed — DI-сид учётки тоже
///        деривация).
/// when:  100 последовательных POST /api/v1/auth/login с верными учётными данными
///        (Stopwatch на каждый; login-лимитер учитывает только НЕудачи — FR-004).
/// then:  p95 ≤ 300 мс (отсутствие лишних дериваций и накладных расходов;
///        NFR-008 verification: бенчмарк в dotnet test, ADR-016).
///
/// Замеряются ТОЛЬКО успешные (2xx) ответы логина: неудачный логин не является
/// предметом NFR-008, поэтому не-2xx фиксируется отдельным падением (с
/// диагностикой), а не в статистику латентности.
///
/// Предусловие тестовых параметров KDF фиксируется утверждением: эффективное
/// число итераций читается из хранимого хэша учётки сида (формат FR-005:
/// 'pbkdf2-sha256$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;'; Verify
/// деривирует по параметрам самого хэша). Без этой фиксации бенчмарк прошёл бы
/// вакуумно: если UseSetting("Auth:Pbkdf2Iterations","1000") не применился, логин
/// идёт при итерациях по умолчанию (~210000, ~60–150 мс на современном CPU) и всё
/// равно укладывается в бюджет 300 мс, теряя предмет проверки — «отсутствие
/// лишних дериваций при тестовых параметрах KDF».
/// </summary>
public sealed class Ts193_LoginBenchmarkTests : IClassFixture<Ts193_LoginBenchmarkTests.Fixture>
{
    private const int Requests = 100;
    private const double LatencyBudgetMs = 300.0;

    /// <summary>Тестовые параметры KDF кейса (given TS-193: Auth__Pbkdf2Iterations=1000).</summary>
    private const int TestKdfIterations = 1000;

    private const string Login = "b193-bench-user";
    private const string Password = "B193-Bench-Password1!";

    private readonly Fixture _fixture;

    public Ts193_LoginBenchmarkTests(Fixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Фикстура: стенд Auth__Pbkdf2Iterations=1000 + пользователь сида.</summary>
    public sealed class Fixture : IDisposable
    {
        public B22WebAppFactory.LoginBenchmark Factory { get; } = new();

        public Fixture()
        {
            // Пользователь сида: DI-сид через репозиторий; хэш — реальным
            // IPasswordHasher стенда (учётка пригодна для верного логина).
            // Метка seed (ADR-007): DI-сид учётки теста — тоже деривация.
            var users = Factory.Services.GetRequiredService<IUserRepository>();
            if (users.GetByLogin(Login) is null)
            {
                var hasher = Factory.Services.GetRequiredService<IPasswordHasher>();
                users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    Login = Login,
                    Email = $"{Login}@example.com",
                    FullName = "B193 Бенчмарк Пользователь",
                    Role = UserRoles.Student,
                    GroupId = null,
                    PasswordHash = hasher.Hash(Password, KdfCallers.Seed),
                    CreatedAt = DateTime.UtcNow,
                });
            }
        }

        public void Dispose() => Factory.Dispose();
    }

    [Fact]
    public async Task HundredSequentialLogins_WithCorrectCredentials_P95_IsAtMost300Ms()
    {
        // given: стенд с Auth__Pbkdf2Iterations=1000; пользователь сида существует.
        // Предусловие тестовых параметров KDF фиксируется утверждением —
        // эффективное число итераций читается из хэша учётки, по которой логинится
        // бенчмарк (деривации Verify идут по параметрам этого хэша).
        var storedUser = _fixture.Factory.Services.GetRequiredService<IUserRepository>().GetByLogin(Login);
        Assert.True(
            storedUser is not null,
            "Предусловие given: пользователь сида не найден в DI-хранилище тестового хоста.");
        var effectiveIterations = ExtractStoredHashIterations(storedUser!.PasswordHash);
        Assert.True(
            effectiveIterations == TestKdfIterations,
            $"Предусловие given TS-193 не исполнимо: стенд работает не при {TestKdfIterations} итерациях KDF — "
            + $"UseSetting(\"Auth:Pbkdf2Iterations\",\"{TestKdfIterations}\") не отразился на хранимом хэше "
            + $"сида (эффективно: {effectiveIterations} итераций, хэш: '{storedUser.PasswordHash}'). "
            + "NFR-008 теряет предмет проверки «отсутствие лишних дериваций при тестовых параметрах KDF»: "
            + "бенчмарк при итерациях по умолчанию прошёл бы вакуумно.");

        using var client = B22Sessions.Create(_fixture.Factory);
        var latencies = new List<double>(Requests);
        var failures = new List<string>();

        // when: 100 последовательных POST /auth/login с верными учётными данными.
        for (var i = 0; i < Requests; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
            {
                login = Login,
                password = Password,
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
        // предметом NFR-008 (верные учётные данные, отсутствие лишних дериваций).
        Assert.True(
            failures.Count == 0,
            "Предусловие NFR-008 нарушено — POST /api/v1/auth/login с верными учётными "
            + $"данными не успешен ({failures.Count} из {Requests}): "
            + string.Join(" | ", failures.Take(5)));

        // then: p95 ≤ 300 мс.
        var sorted = latencies.OrderBy(value => value).ToArray();
        Assert.True(
            sorted.Length > 0,
            "Нет ни одного успешного замера латентности логина.");
        var index = Math.Max(0, (int)Math.Ceiling(0.95 * sorted.Length) - 1);
        var p95 = sorted[index];
        Assert.True(
            p95 <= LatencyBudgetMs,
            $"NFR-008: p95 POST /api/v1/auth/login = {p95.ToString("F1", CultureInfo.InvariantCulture)} мс "
            + $"(бюджет — 300 мс), медиана = {sorted[sorted.Length / 2].ToString("F1", CultureInfo.InvariantCulture)} мс, "
            + $"замеров = {sorted.Length}.");
    }

    /// <summary>
    /// Эффективное число итераций KDF из хранимой строки хэша. Формат FR-005:
    /// 'pbkdf2-sha256$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;' —
    /// параметры в самой строке; разделитель в текущем срезе реализации может
    /// отличаться ('$' или '|'), позиция параметра итераций — вторая.
    /// Нераспознанный формат даёт -1: предусловие честно падает с диагностикой,
    /// а не проходит мимо.
    /// </summary>
    private static int ExtractStoredHashIterations(string storedHash)
    {
        var parts = storedHash.Split('$', '|');
        return parts.Length == 4
            && string.Equals(parts[0], "pbkdf2-sha256", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
                ? iterations
                : -1;
    }
}
