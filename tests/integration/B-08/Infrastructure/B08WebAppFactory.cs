using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B08.Infrastructure;

/// <summary>
/// Тестовый хост батча B-08 (зона tests/integration/B-08): собственная копия
/// механики фабрики (фабрики зон B-01..B-07 и src/api/LabsApp.Tests — internal
/// и чужие зоны; изоляция по образцу зон B-01/B-07, BL-001 BUG-001). Окружение
/// по умолчанию — Development (кейсы TS-061..TS-070, TS-072..TS-075); кейс
/// TS-071 использует <see cref="B08ProductionWebAppFactory"/> (Production),
/// кейс TS-074 — <see cref="B08LimiterWebAppFactory"/> (явный потолок ключей).
/// Auth__JwtKey и Seed__TeacherPassword зафиксированы (ADR-010);
/// Seed__DemoData=false ЯВНО — кейсы батча не зависят от демо-набора
/// (AR-011/NFR-011); пользователи создаются DI-сидом (кейс TS-066: «DI-сид»),
/// сессии — минтом access-JWT через ITokenService без зависимости от
/// POST /auth/login (ADR-022: logout/me/recovery — доменные задачи волны,
/// где login может отсутствовать). Content root — выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// Log-sink (кейсы TS-069..TS-072 — инспекция журнала): каждый тестовый хост
/// получает <see cref="B08LogSink"/> (LogSink), собирающий записи всех категорий;
/// доступ — из свойства LogSink, изоляция — LogSink.Clear() перед сценарием.
///
/// Изоляция сценариев (лимитер запросов кода 3/час на email_ci FR-080;
/// хранилище in-memory): каждый тестовый КЛАСС со своей IClassFixture-фикстурой
/// получает свежий экземпляр приложения — пустые словари лимитеров, пустое
/// хранилище (кроме сид-преподавателя) и чистый sink, поэтому выбранные кейсами
/// email свободны/заняты ровно так, как сказано в given.
/// </summary>
public class B08WebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b08-integration-test-jwt-signing-key-0123456789abcdef";
    public const string TestTeacherPassword = "b08-test-teacher-password1!";

    /// <summary>Log-sink тестового хоста; изоляция — Clear() перед сценарием.</summary>
    public B08LogSink LogSink { get; } = new();

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B08WebAppFactory()
        : this(Environments.Development, new Dictionary<string, string?>())
    {
    }

    /// <summary>Внутренний конструктор для производных фикстур (окружение/настройки).</summary>
    protected B08WebAppFactory(string environment, IReadOnlyDictionary<string, string?> settings)
    {
        // Урок BL-001 (среда прогона): машина конвейера делит лимит
        // fs.inotify.max_user_instances (128) между параллельными батчами и
        // сессией оператора — создание экземпляра хоста падает на
        // PhysicalFilesWatcher, когда лимит исчерпан. Документированный
        // переключатель DOTNET_USE_POLLING_FILE_WATCHER переводит провайдеры
        // файлов хоста на опрос без inotify; на тестируемые HTTP-контракты
        // наблюдение за файлами конфигурации не влияет. Хост живёт в процессе
        // testhost — переменная процесса применяется ко всем провайдерам.
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");

        _environment = environment;
        _settings = settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);

        // Кейсы батча опираются только на собственных пользователей DI-сида:
        // демо-набор отключён ЯВНО — детерминизм независимо от умолчаний
        // окружения и быстрый старт (AR-011/NFR-011).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        // Log-sink в конвейере журналирования тестового хоста (TS-069..TS-072).
        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(ToConfigKey(key), value);
        }
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}

/// <summary>
/// Production-фикстура кейса TS-071 (окружение Production, «валидные секреты»):
/// Auth__JwtKey задан явно (FR-030: ≥32 символа, не dev-ключ),
/// Seed__TeacherPassword задан явно и удовлетворяет правилам §8
/// (FR-006: Production-guard). Seed__DemoData не задаётся: в Production флаг
/// всегда трактуется как false (FR-007) — проверяется самим стартом хоста.
/// </summary>
public sealed class B08ProductionWebAppFactory : B08WebAppFactory
{
    public const string ProductionJwtKey = "b08-production-integration-jwt-signing-key-0123456789";
    public const string ProductionTeacherPassword = "b08-prod-teacher-password1!";

    public B08ProductionWebAppFactory()
        : base(
            Environments.Production,
            new Dictionary<string, string?>
            {
                [AuthOptions.JwtKeyVariable] = ProductionJwtKey,
                [SeedOptions.TeacherPasswordVariable] = ProductionTeacherPassword,
            })
    {
    }
}

/// <summary>
/// Фикстура кейса TS-074 (Development): потолок ключей лимитера
/// MaxTrackedKeys=10000 — КОНСТАНТА движка SlidingWindowLimiter (ADR-005),
/// конфигурация RateLimits__* удалена из API; фикстура сохранена как явный
/// Development-хост кейса — потолок фиксирован независимо от окружения.
/// </summary>
public sealed class B08LimiterWebAppFactory : B08WebAppFactory
{
    public B08LimiterWebAppFactory()
        : base(Environments.Development, new Dictionary<string, string?>())
    {
    }
}
