using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B02.Infrastructure;

/// <summary>
/// Тестовый хост батча B-02 (зона tests/integration/B-02): собственная копия механики
/// фабрики (фабрика src/api/LabsApp.Tests — internal и чужая зона; изоляция по образцу
/// зоны B-01, BL-001 BUG-001).
///
/// Харнес-умолчание (NFR-011): Seed__DemoData=false — автотесты не зависят от
/// демо-сида (его состав не вычисляется на каждый хост). Кейсы демо-сида
/// (TS-138 counts/parity, TS-139) создают фабрику с useHarnessDefaults:false
/// (Seed__DemoData остаётся не заданным — работает умолчание Development) и
/// переопределяют настройки через settings — поздние значения выигрывают.
///
/// Аутентификации в приложении нет: секретные переменные (Auth__*/Seed__TeacherPassword)
/// не существуют, Production-умолчанию достаточно явной строки подключения хранилища.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер переменных
/// окружения). Content root — выходной каталог тестов.
/// </summary>
public sealed class B02WebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Харнес-умолчание Seed__DemoData (NFR-011). Значение — "false": батч по
    /// умолчанию не зависит от демо-сида; кейсы TS-138 (counts/parity) и TS-139
    /// используют useHarnessDefaults:false.
    /// </summary>
    public const string HarnessDemoDataSettingName = "Seed:DemoData";

    /// <summary>
    /// Ключ конфигурации хостинга «hostBuilder:reloadConfigOnChange» — отключение
    /// файловых watch'ей конфигурации (адаптация окружения, см. ConfigureWebHost).
    /// </summary>
    public const string HostDefaultsReloadConfigOnChangeKey = "hostBuilder:reloadConfigOnChange";

    private readonly string _environment;
    private readonly bool _useHarnessDefaults;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>
    /// Информационный замер времени инициализации хоста (NFR-011): заполняется в
    /// <see cref="CreateWarmClient"/>; ЖЁСТКИЙ assert на значение НЕ используется
    /// (анти-флейк) — значение только фиксируется и пишется в журнал прогона.
    /// </summary>
    public TimeSpan? HostWarmupDuration { get; private set; }

    /// <summary>
    /// Единственный публичный конструктор без аргументов — для IClassFixture (xUnit):
    /// Development с харнес-умолчаниями (Seed__DemoData=false).
    /// </summary>
    public B02WebAppFactory()
        : this(Environments.Development, useHarnessDefaults: true, settings: null)
    {
    }

    /// <summary>Конструктор для отдельных кейсов: окружение, харнес-умолчания, settings
    /// (internal: у типа фикстуры xUnit обязан быть ровно один публичный конструктор).</summary>
    internal B02WebAppFactory(
        string environment,
        bool useHarnessDefaults = true,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        _environment = environment;
        _useHarnessDefaults = useHarnessDefaults;
        _settings = settings ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Хранилище приложения — PostgreSQL-only; тесты зоны работают на in-memory
        // двойках tests/TestInfrastructure (без ввода-вывода и внешней БД).
        builder.ConfigureServices(s => LabsApp.TestInfrastructure.InMemoryTestStorage.Register(s));
        // Явная строка подключения (не равна dev-умолчанию): Production-режим
        // тестовых хостов проходит StorageOptionsValidator; двойки хранилища
        // подключений не выполняют.
        builder.UseSetting("Storage:ConnectionString", "Host=127.0.0.1;Port=5434;Database=labsapp-test-double");
        builder.UseEnvironment(_environment);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Адаптация окружения прогона: отключает inotify-watcher провайдеров
        // конфигурации (appsettings.json, reloadOnChange). Значения конфигурации
        // читаются как обычно — проверяемое поведение не меняется; на хосте прогона
        // исчерпан пользовательский лимит
        // inotify-инстансов (fs.inotify.max_user_instances), и WebApplication без
        // этого ключа падает на создании FileSystemWatcher (System.IO.IOException).
        builder.UseSetting(HostDefaultsReloadConfigOnChangeKey, "false");

        if (_useHarnessDefaults)
        {
            // NFR-011: харнес не зависит от демо-сида (умолчание Seed__DemoData=false).
            builder.UseSetting(HarnessDemoDataSettingName, "false");
        }

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(ToConfigKey(key), value);
        }
    }

    /// <summary>
    /// Клиент тестового хоста с информационным замером времени инициализации
    /// (NFR-011: «информационный замер в CI-логе времени создания
    /// WebApplicationFactory-хоста; жёсткий автотест на время НЕ вводится»).
    /// Замер пишется в стандартный вывод прогона (CI-лог); значение сохраняется в
    /// <see cref="HostWarmupDuration"/>; ограничений на значение не проверяется.
    /// Клиент — без авто-редиректов; запросы уходят анонимно (аутентификации нет).
    /// </summary>
    public HttpClient CreateWarmClient()
    {
        var stopwatch = Stopwatch.StartNew();
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        stopwatch.Stop();

        HostWarmupDuration = stopwatch.Elapsed;
        Console.WriteLine(
            "[B-02][NFR-011] Информационный замер (без assert на время): инициализация "
            + $"WebApplicationFactory-хоста (build + seed) — {HostWarmupDuration.Value.TotalMilliseconds:F0} мс.");

        return client;
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
