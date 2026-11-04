using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B05.Infrastructure;

/// <summary>
/// Тестовый хост батча B-05 (зона tests/integration/B-05): собственная копия механики
/// фабрики (фабрика src/api/LabsApp.Tests.TestWebAppFactory — internal и чужая зона;
/// изоляция по образцу зон B-01/B-02, BL-001 BUG-001). Окружение — Development
/// (демо-сид включён по умолчанию, FR-004); content root — выходной каталог тестов.
/// Аутентификации в приложении нет: запросы анонимны, «текущий пользователь»
/// (/me/**, updatedBy сдач) — сид-преподаватель (Seed__TeacherLogin).
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// Каждый тестовый КЛАСС со своей IClassFixture-фикстурой получает свежий
/// экземпляр приложения и пустое хранилище (кроме сид-преподавателя и демо-набора)
/// — изоляция given сценариев.
/// </summary>
public sealed class B05WebAppFactory : WebApplicationFactory<Program>
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

        // Изоляция прогона: без file-watcher'ов конфигурации — на хосте с параллельными
        // батчами лимит inotify-инстансов ОС (128) исчерпывается, и хост не поднимается
        // (IOException в JsonConfigurationSource.Build); автотестам reload не нужен.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");
    }
}
