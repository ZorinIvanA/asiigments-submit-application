using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B03.Infrastructure;

/// <summary>
/// Тестовый хост батча B-03 (зона tests/integration/B-03; кейсы TS-088..TS-094 —
/// список/создание работ FR-017): собственная фабрика зоны (изоляция тестовых зон,
/// BL-001 BUG-001), окружение Development, Seed__DemoData=false ЯВНО — данные given
/// кейсов наполняются DI-сидом (<see cref="B03DomainSeed"/>), состав хранилища
/// детерминирован. Аутентификации в приложении нет — кейсы ходят в API анонимно.
///
/// При минимальном хостинге (WebApplicationBuilder) значения UseSetting фабрики
/// попадают в аргументы точки входа до запуска Program, поэтому ключи передаются
/// с разделителем ':' (иерархию '__' создаёт только провайдер переменных окружения).
/// </summary>
public class B03HostFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Хранилище приложения — PostgreSQL-only; тесты зоны работают на in-memory
        // двойках tests/TestInfrastructure (без ввода-вывода и внешней БД).
        builder.ConfigureServices(s => LabsApp.TestInfrastructure.InMemoryTestStorage.Register(s));
        // Явная строка подключения (не равна dev-умолчанию): Production-режим
        // тестовых хостов проходит StorageOptionsValidator; двойки хранилища
        // подключений не выполняют.
        builder.UseSetting("Storage:ConnectionString", "Host=127.0.0.1;Port=5434;Database=labsapp-test-double");
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);
        // Наблюдатель reload-on-change appsettings.json не нужен тестовому хосту и
        // исчерпывает inotify-лимит машины прогона (по экземпляру на каждый из
        // изолированных хостов IClassFixture) — отключается для стабильности харнеса;
        // на проверяемые HTTP-контракты не влияет.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");
        builder.UseSetting(ConfigKey(SeedOptions.DemoDataVariable), "false");
    }

    /// <summary>'__'-имя переменной окружения → ':'-ключ конфигурации (см. шапку класса).</summary>
    protected static string ConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
