using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B07.Infrastructure;

/// <summary>
/// Тестовый хост батча B-07 (зона tests/integration/B-07): собственная копия механики
/// фабрики (фабрики зон B-01..B-05 и src/api/LabsApp.Tests — internal и чужие зоны;
/// изоляция по образцу зон B-01/B-02, BL-001 BUG-001). Окружение по умолчанию —
/// Development (кейсы TS-089..TS-094, TS-193, TS-194); кейс TS-095 использует
/// <see cref="B07ProductionWebAppFactory"/> (окружение Production). Auth__JwtKey и
/// Seed__TeacherPassword зафиксированы (ADR-010); Seed__DemoData=false ЯВНО —
/// кейсы батча не зависят от демо-набора (AR-011/NFR-011), пользователи и
/// «занятые email» создаются регистрацией через публичный API в own-фикстуре
/// каждого тестового класса. Content root — выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// Log-sink (кейсы TS-093/TS-094/TS-095 — инспекция журнала): каждый тестовый хост
/// получает <see cref="B07LogSink"/> (LogSink), собирающий записи всех категорий;
/// доступ — из свойства LogSink, изоляция — LogSink.Clear() перед сценарием.
///
/// Изоляция сценариев (лимит регистраций 5/3600с на IP — матрица FR-004,
/// применяется в FR-006 до валидации тела; хранилище in-memory — IF-015):
/// каждый тестовый КЛАСС со своей IClassFixture-фикстурой получает свежий экземпляр
/// приложения — пустые счётчики, пустое хранилище (кроме сид-преподавателя) и
/// чистый sink, поэтому выбранные кейсами email свободны/заняты ровно так, как
/// сказано в given.
/// </summary>
public class B07WebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b07-integration-test-jwt-signing-key-0123456789abcdef";
    public const string TestTeacherPassword = "b07-test-teacher-password1!";

    /// <summary>Log-sink тестового хоста; изоляция — Clear() перед сценарием.</summary>
    public B07LogSink LogSink { get; } = new();

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B07WebAppFactory()
        : this(Environments.Development, new Dictionary<string, string?>())
    {
    }

    /// <summary>Внутренний конструктор для производных фикстур (окружение/настройки).</summary>
    protected B07WebAppFactory(string environment, IReadOnlyDictionary<string, string?> settings)
    {
        _environment = environment;
        _settings = settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Изоляция прогона: без file-watcher'ов конфигурации — на хосте с параллельными
        // батчами лимит inotify-инстансов ОС (128) исчерпывается, и тестовый хост не
        // поднимается (IOException в JsonConfigurationSource.Build); автотестам
        // reload конфигов не нужен (механика зон B-01..B-06).
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);

        // Кейсы батча опираются только на собственных пользователей, созданных
        // регистрацией через публичный API: демо-набор отключён ЯВНО — детерминизм
        // независимо от умолчаний окружения и быстрее старт (AR-011/NFR-011).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        // Log-sink в конвейере журналирования тестового хоста (инспекция TS-093/094/095).
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
/// Production-фикстура кейса TS-095 (окружение Production): Auth__JwtKey задан
/// явно (IF-003: ключ Auth__JwtKey ≥32 байт, не dev-ключ), Seed__TeacherPassword
/// задан явно и отличается от умолчания (FR-028(3): guard прод-конфигурации —
/// в Production старт блокируется при незаданном или дефолтном
/// Seed__TeacherPassword). Seed__DemoData не задаётся: демо-набор кейсу не
/// нужен — проверяется самим стартом хоста.
/// </summary>
public sealed class B07ProductionWebAppFactory : B07WebAppFactory
{
    public const string ProductionJwtKey = "b07-production-integration-jwt-signing-key-0123456789";
    public const string ProductionTeacherPassword = "b07-prod-teacher-password1!";

    public B07ProductionWebAppFactory()
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
