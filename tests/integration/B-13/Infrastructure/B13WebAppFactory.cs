using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>
/// Тестовый хост батча B-13 (зона tests/integration/B-13): собственная копия механики
/// фабрики (фабрики зон B-01..B-11 и src/api/LabsApp.Tests — internal и чужие зоны;
/// изоляция по образцу зон B-01/B-04/B-10/B-11, BL-001 BUG-001). Окружение —
/// Development; content root — выходной каталог тестов. Демо-сид выключен ЯВНО
/// (Seed__DemoData=false, AR-011/NFR-011: автотесты не зависят от демо-набора) —
/// все кейсы TS-148..TS-152 и TS-160/TS-162 наполняются DI-сидом с точными
/// количествами (7 студентов, 4 сдачи, лабы №1–3 семестра 1). Аутентификации в
/// приложении нет: клиенты анонимны, auth-настройки хосту не нужны.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
/// </summary>
public sealed class B13WebAppFactory : WebApplicationFactory<Program>
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

        // Короткоживущие тестовые хосты не перезагружают конфигурацию (как в зоне
        // B-01 и в B13ScopeWebAppFactory): reloadOnChange создаёт inotify-инстансы,
        // исчерпание системного лимита валит построение хоста с IOException.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
