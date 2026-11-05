using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B14.Infrastructure;

/// <summary>
/// Тестовый хост батча B-14 (зона tests/integration/B-14): собственная копия механики
/// фабрики (фабрики зон B-01..B-15 и src/api/LabsApp.Tests — internal и чужие зоны;
/// изоляция по образцу зон B-01/B-02, BL-001 BUG-001). Окружение — Development.
/// Seed__DemoData=false ЯВНО — кейсы батча не зависят от демо-набора (AR-011/NFR-011):
/// сид-преподаватель (текущий пользователь профиля) сидится всегда и идемпотентно
/// (FR-004). Аутентификации в приложении нет — auth-настройки хосту не нужны.
/// Content root — выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
/// </summary>
public class B14WebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B14WebAppFactory()
    {
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
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Кейсы батча опираются на сид-преподавателя (TS-196): демо-набор отключён
        // ЯВНО — детерминизм независимо от умолчаний окружения и быстрее старт
        // (AR-011/NFR-011).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
