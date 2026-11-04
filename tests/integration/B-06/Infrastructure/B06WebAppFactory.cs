using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Тестовый хост батча B-06 (зона tests/integration/B-06): собственная копия механики
/// фабрики (фабрики зон B-01..B-07 и src/api/LabsApp.Tests — internal и чужие зоны;
/// изоляция по образцу зон B-01/B-02, BL-001 BUG-001). Окружение — Development
/// (teacher-сид FR-006); Seed__DemoData=false ЯВНО — кейсы батча не зависят от
/// демо-набора (AR-011/NFR-011), данные создаются самим сидом преподавателя.
/// Content root — выходной каталог тестов. Аутентификации в приложении нет:
/// настройка auth-ключей и подмена RemoteIpAddress (лимиты регистраций) не нужны.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер переменных
/// окружения).
///
/// Изоляция сценариев: каждый тестовый КЛАСС со своей IClassFixture-фикстурой
/// получает свежий экземпляр приложения — пустое хранилище (кроме сид-преподавателя),
/// поэтому выбранные кейсами сущности свободны ровно так, как сказано в given.
/// </summary>
public sealed class B06WebAppFactory : WebApplicationFactory<Program>
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

        // Окружение прогона не предоставляет inotify-инстансы надёжно (пер-пользовательский
        // лимит 128 исчерпывается параллельными батчами): файловые вотчеры перечитывания
        // конфигурации тестовому хосту не нужны — Options читаются при старте
        // (ValidateOnStart), проверяемые HTTP-контракты от hot-reload не зависят.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        // AR-011/NFR-011: кейсы батча опираются только на сид-преподавателя (FR-006,
        // идемпотентный) — демо-набор отключён.
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
