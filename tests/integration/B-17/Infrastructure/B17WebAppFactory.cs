using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B17.Infrastructure;

/// <summary>
/// Тестовый хост батча B-17 (зона tests/integration/B-17): собственная копия
/// механики фабрики (фабрики зон B-01..B-21 и src/api/LabsApp.Tests — internal
/// и чужие зоны; изоляция по образцу зон B-08/B-21, BL-001 BUG-001). Окружение
/// по умолчанию — Development. Аутентификации в приложении нет: все эндпоинты
/// публичны, «текущий пользователь» — засеянный преподаватель (Seed__TeacherLogin,
/// умолчание «teacher»); сценарии зоны ходят анонимным клиентом. Seed__DemoData=false
/// ЯВНО — кейсы батча не зависят от демо-набора; пользователи создаются DI-сидом
/// (ADR-015). Content root — выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// Log-sink (инспекция журнала по категориям): каждый тестовый хост получает
/// <see cref="B17LogSink"/>, собирающий записи всех категорий; доступ — из
/// свойства LogSink, изоляция — LogSink.Clear() перед сценарием.
///
/// Изоляция сценариев (хранилище in-memory — FR-024): каждый тестовый КЛАСС со
/// своей IClassFixture-фикстурой получает свежий экземпляр приложения — пустое
/// хранилище (кроме сид-преподавателя) и чистый sink, поэтому выбранные кейсами
/// email свободны/заняты ровно так, как сказано в given.
/// </summary>
public class B17WebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Log-sink тестового хоста; изоляция — Clear() перед сценарием.</summary>
    public B17LogSink LogSink { get; } = new();

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B17WebAppFactory()
        : this(Environments.Development, new Dictionary<string, string?>())
    {
    }

    /// <summary>Внутренний конструктор для производных фикстур (окружение/настройки).</summary>
    protected B17WebAppFactory(string environment, IReadOnlyDictionary<string, string?> settings)
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
        // Хранилище приложения — PostgreSQL-only; тесты зоны работают на in-memory
        // двойках tests/TestInfrastructure (без ввода-вывода и внешней БД).
        builder.ConfigureServices(s => LabsApp.TestInfrastructure.InMemoryTestStorage.Register(s));
        // Явная строка подключения (не равна dev-умолчанию): Production-режим
        // тестовых хостов проходит StorageOptionsValidator; двойки хранилища
        // подключений не выполняют.
        builder.UseSetting("Storage:ConnectionString", "Host=127.0.0.1;Port=5434;Database=labsapp-test-double");
        builder.UseEnvironment(_environment);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Кейсы батча опираются только на собственных пользователей DI-сида:
        // демо-набор отключён ЯВНО — детерминизм независимо от умолчаний
        // окружения и быстрый старт (AR-011/NFR-011).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        // Log-sink в конвейере журналирования тестового хоста (инспекция журнала).
        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(ToConfigKey(key), value);
        }
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
