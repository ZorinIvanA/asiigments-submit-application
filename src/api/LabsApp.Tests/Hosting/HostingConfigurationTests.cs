using System.Diagnostics;
using System.Net;
using LabsApp.Auth;
using LabsApp.Auth.RateLimiting;
using LabsApp.Hosting;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LabsApp.Tests.Hosting;

/// <summary>
/// Интеграционные проверки FR-006/FR-025: fail-fast конфигурации в Production
/// (до обслуживания запросов, сообщение называет переменную), умолчания в
/// Development, эпизодический случайный dev-ключ JWT + warning, guard
/// Seed__TeacherPassword; RateLimits__* игнорируется как неизвестная секция
/// (T-101: потолок движка — константа, ADR-005).
/// </summary>
public sealed class HostingConfigurationTests
{
    // Категория warnings конфигурации — из общего реестра (CR-001: без префикса
    // пространства имён, дословно «Hosting.Configuration»).
    private const string ConfigurationCategory = HostingLogCategories.Configuration;

    // ADR-005/T-101: переменная удалена из конфигурации; имя среды используется
    // в проверке «неизвестная секция игнорируется, старт успешен».
    private const string RateLimitsMaxTrackedKeysVariable = "RateLimits__MaxTrackedKeys";

    [Fact]
    public async Task Production_ValidSecrets_StartsAndServesHealth()
    {
        using var factory = new TestWebAppFactory(Environments.Production);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------------
    // FR-006: ветки Auth__JwtKey.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("k")]
    [InlineData("kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk")]
    public void Production_MissingOrShortJwtKey_FailsFastWithVariableName(string? jwtKey)
    {
        var factory = new TestWebAppFactory(
            Environments.Production,
            Settings((AuthOptions.JwtKeyVariable, jwtKey)));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains(Describe(exception), m => m.Contains(AuthOptions.JwtKeyVariable, StringComparison.Ordinal));
    }

    // NFR-007: отказ старта при невалидной конфигурации Production — ≤ 5 с,
    // исключение называет переменную.
    [Fact]
    public void Production_InvalidJwtKey_StartupRefusedWithinFiveSeconds()
    {
        var factory = new TestWebAppFactory(
            Environments.Production,
            Settings((AuthOptions.JwtKeyVariable, "short")));

        var stopwatch = Stopwatch.StartNew();
        var exception = Record.Exception(() => factory.CreateClient());
        stopwatch.Stop();

        Assert.NotNull(exception);
        Assert.Contains(Describe(exception), m => m.Contains(AuthOptions.JwtKeyVariable, StringComparison.Ordinal));
        Assert.True(
            stopwatch.Elapsed <= TimeSpan.FromSeconds(5),
            $"Отказ старта занял {stopwatch.Elapsed.TotalSeconds:F2} с; NFR-007 требует не более 5 с.");
    }

    // FR-006: в Development без Auth__JwtKey хост подставляет эпизодический
    // случайный ключ — разные хосты получают РАЗНЫЕ ключи; старт и обслуживание.
    [Fact]
    public async Task Development_MissingJwtKey_SubstitutesEphemeralRandomKeyWithWarning()
    {
        var first = new TestWebAppFactory(settings: Settings((AuthOptions.JwtKeyVariable, string.Empty)));
        var second = new TestWebAppFactory(settings: Settings((AuthOptions.JwtKeyVariable, string.Empty)));

        using (first)
        using (second)
        {
            var firstKey = first.Services.GetRequiredService<IOptions<AuthOptions>>().Value.JwtKey;
            var secondKey = second.Services.GetRequiredService<IOptions<AuthOptions>>().Value.JwtKey;

            Assert.False(string.IsNullOrWhiteSpace(firstKey));
            Assert.True(firstKey!.Length >= AuthOptions.JwtKeyMinLength);
            Assert.NotEqual(firstKey, secondKey);

            // Warning в журнале конфигурации; значение ключа в записях отсутствует.
            var warnings = first.LogSink.Snapshot()
                .Where(record => record.Category == ConfigurationCategory
                    && record.Level == Microsoft.Extensions.Logging.LogLevel.Warning)
                .ToList();
            Assert.Contains(warnings, record => record.Message.Contains(
                AuthOptions.JwtKeyVariable, StringComparison.Ordinal));
            Assert.All(
                warnings,
                record => Assert.DoesNotContain(firstKey, record.Message, StringComparison.Ordinal));

            using var client = first.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        }
    }

    // Явно заданный ключ в Development подставляется как есть, без warning.
    [Fact]
    public async Task Development_ExplicitJwtKey_UsedVerbatimWithoutWarning()
    {
        using var factory = new TestWebAppFactory(); // TestWebAppFactory всегда задаёт JwtKey

        var key = factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value.JwtKey;

        Assert.Equal(TestWebAppFactory.TestJwtKey, key);
        Assert.DoesNotContain(
            factory.LogSink.Snapshot(),
            record => record.Category == ConfigurationCategory
                && record.Message.Contains(AuthOptions.JwtKeyVariable, StringComparison.Ordinal));
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    // ------------------------------------------------------------------
    // FR-005: ветки Auth__Pbkdf2Iterations (умолчание 210 000; целое
    // от 1 до 10 000 000, любое окружение).
    // ------------------------------------------------------------------

    // Переменная не задана — применяется умолчание 210 000, хост стартует.
    [Fact]
    public async Task Pbkdf2Iterations_Missing_DefaultAppliedAndHostStarts()
    {
        // Умолчания харнеса отключены (T-006) — проверяется умолчание ПРИЛОЖЕНИЯ:
        // Auth__Pbkdf2Iterations остаётся не заданной.
        using var factory = new TestWebAppFactory(infrastructureDefaults: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var authOptions = factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        Assert.Equal(AuthOptions.DefaultPbkdf2Iterations, authOptions.Pbkdf2Iterations);
    }

    // Пустая строка — не «отсутствие», а невалидное значение наравне с
    // нечисловым: отказ старта, ошибка привязки называет ключ конфигурации.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void Pbkdf2Iterations_NonNumeric_FailsFastWithVariableName(string value)
    {
        var factory = new TestWebAppFactory(
            settings: Settings((AuthOptions.Pbkdf2IterationsVariable, value)));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains(Describe(exception), m => m.Contains("Pbkdf2Iterations", StringComparison.Ordinal));
    }

    // Вне диапазона (0 и 10 000 001) — отказ старта с именем переменной.
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10000001")]
    public void Pbkdf2Iterations_OutOfRange_FailsFastWithVariableName(string value)
    {
        var factory = new TestWebAppFactory(
            settings: Settings((AuthOptions.Pbkdf2IterationsVariable, value)));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains(
            Describe(exception),
            m => m.Contains(AuthOptions.Pbkdf2IterationsVariable, StringComparison.Ordinal));
    }

    // Границы диапазона (1 и ровно 10 000 000) — хост стартует, значение применяется.
    [Theory]
    [InlineData("1")]
    [InlineData("10000000")]
    public async Task Pbkdf2Iterations_Boundary_HostStartsAndAppliesValue(string value)
    {
        using var factory = new TestWebAppFactory(
            settings: Settings((AuthOptions.Pbkdf2IterationsVariable, value)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var authOptions = factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        Assert.Equal(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture), authOptions.Pbkdf2Iterations);
    }

    // ------------------------------------------------------------------
    // FR-025 (SEC-001): guard Seed__TeacherPassword в Production.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("teacher123!")]
    public void Production_MissingOrDefaultTeacherPassword_FailsFastWithVariableName(string? password)
    {
        var factory = new TestWebAppFactory(
            Environments.Production,
            Settings((SeedOptions.TeacherPasswordVariable, password)));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains(Describe(exception), m => m.Contains(SeedOptions.TeacherPasswordVariable, StringComparison.Ordinal));
    }

    // SEC-001 (регресс): guard и сид работают с ОДНОЙ строкой — сид-пароль
    // Production с ведущими/хвостовыми пробелами проходит guard (§8 не триммит
    // пароль) и попадает в учётную запись дословно, а не обрезанным до значения,
    // которому проверка не предъявлялась.
    [Fact]
    public void Production_PaddedTeacherPassword_GuardAndSeederUseSameValue()
    {
        const string padded = " padded-teacher-pass1 ";
        using var factory = new TestWebAppFactory(
            Environments.Production,
            Settings((SeedOptions.TeacherPasswordVariable, padded)));
        _ = factory.Services; // построение приложения: guard FR-006 + сид

        var teacher = factory.Services.GetRequiredService<IUserRepository>().GetByLogin("teacher");
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();

        Assert.NotNull(teacher);
        Assert.True(hasher.Verify(padded, teacher!.PasswordHash, KdfCallers.Login));
        Assert.False(hasher.Verify(padded.Trim(), teacher.PasswordHash, KdfCallers.Login));
    }

    // ------------------------------------------------------------------
    // Прочие fail-fast ветки и умолчания.
    // ------------------------------------------------------------------

    [Fact]
    public void Production_TtlNonPositive_FailsFastWithVariableName()
    {
        var factory = new TestWebAppFactory(
            Environments.Production,
            Settings((AuthOptions.AccessTtlMinutesVariable, "0")));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains(Describe(exception), m => m.Contains(AuthOptions.AccessTtlMinutesVariable, StringComparison.Ordinal));
    }

    [Fact]
    public void Production_MaxSemesterOutOfRange_FailsFastWithVariableName()
    {
        var factory = new TestWebAppFactory(
            Environments.Production,
            Settings((LabsOptions.MaxSemesterVariable, "101")));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains(Describe(exception), m => m.Contains(LabsOptions.MaxSemesterVariable, StringComparison.Ordinal));
    }

    // ADR-005/T-101: RateLimits__* больше НЕ конфигурация — потолок ключей движка
    // задан константой SlidingWindowLimiter.MaxTrackedKeys (NFR-003). Неизвестная
    // секция конфигурации игнорируется: значение переменной не влияет на старт
    // приложения и не ограничивает движок («1» не сужает потолок до единицы).
    [Fact]
    public async Task Development_UnknownRateLimitsVariable_StartSucceeds_EngineCeilingUnchanged()
    {
        using var factory = new TestWebAppFactory(
            settings: Settings((RateLimitsMaxTrackedKeysVariable, "1")));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Потолок движка — константа 10000, конфигурацией не переопределяется (NFR-003).
        Assert.Equal(10000, SlidingWindowLimiter.MaxTrackedKeys);
        // Поведенчески: движок допускает БОЛЬШЕ одного индивидуального ключа.
        var limiter = new SlidingWindowLimiter(factory.Services.GetRequiredService<TimeProvider>());
        Assert.True(limiter.TryAcquire("rate-limits-key-1", windowMs: 60_000, limit: 5));
        Assert.True(limiter.TryAcquire("rate-limits-key-2", windowMs: 60_000, limit: 5));
    }

    [Fact]
    public void Development_Defaults_Applied()
    {
        // Умолчания харнеса отключены (T-006): проверяются умолчания ПРИЛОЖЕНИЯ
        // (композиция-корень/окружение), а не умолчания тестовой фабрики.
        using var factory = new TestWebAppFactory(infrastructureDefaults: false);

        var authOptions = factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        var labsOptions = factory.Services.GetRequiredService<IOptions<LabsOptions>>().Value;
        var seedOptions = factory.Services.GetRequiredService<IOptions<SeedOptions>>().Value;

        Assert.Equal(AuthOptions.DefaultAccessTtlMinutes, authOptions.AccessTtlMinutes);
        Assert.Equal(AuthOptions.DefaultRefreshTtlDays, authOptions.RefreshTtlDays);
        Assert.Equal(AuthOptions.DefaultPbkdf2Iterations, authOptions.Pbkdf2Iterations);
        Assert.Equal(LabsOptions.DefaultMaxSemester, labsOptions.MaxSemester);
        Assert.Equal(SeedOptions.DefaultTeacherLogin, seedOptions.TeacherLogin);
        Assert.False(SeedOptions.IsKnownDemoDataValue(seedOptions.DemoData));
        Assert.True(seedOptions.ResolveDemoData(inDevelopment: true));
        Assert.False(seedOptions.ResolveDemoData(inDevelopment: false));
    }

    // Единый источник бизнес-времени: TimeProvider зарегистрирован в DI
    // композиция-корня и переопределяется тестами подменой (tech solution «время»).
    [Fact]
    public void TimeProvider_RegisteredAsSystem_InCompositionRoot()
    {
        using var factory = new TestWebAppFactory();

        var timeProvider = factory.Services.GetRequiredService<TimeProvider>();

        Assert.Same(TimeProvider.System, timeProvider);
    }

    [Fact]
    public async Task Development_UnknownSeedDemoData_DoesNotFailStartup()
    {
        using var factory = new TestWebAppFactory(
            settings: Settings((SeedOptions.DemoDataVariable, "maybe")));

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var seedOptions = factory.Services.GetRequiredService<IOptions<SeedOptions>>().Value;
        Assert.Equal("maybe", seedOptions.DemoData);
        Assert.False(SeedOptions.IsKnownDemoDataValue(seedOptions.DemoData));
    }

    private static IReadOnlyDictionary<string, string?> Settings(params (string Key, string? Value)[] items) =>
        items.ToDictionary(item => item.Key, item => item.Value);

    private static IReadOnlyCollection<string> Describe(Exception? exception)
    {
        var messages = new List<string>();
        var pending = new Queue<Exception?>();
        pending.Enqueue(exception);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (current is null)
            {
                continue;
            }

            messages.Add(current.Message);
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Enqueue(inner);
                }

                continue;
            }

            pending.Enqueue(current.InnerException);
        }

        return messages;
    }
}
