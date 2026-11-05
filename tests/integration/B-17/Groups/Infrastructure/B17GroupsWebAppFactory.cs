using LabsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B17.Groups.Infrastructure;

/// <summary>
/// Тестовый хост сценариев групп батча B-17 (зона tests/integration/B-17/Groups).
/// Окружение — Development. Аутентификации в приложении нет: все эндпоинты
/// публичны, «текущий пользователь» — засеянный преподаватель
/// (Seed__TeacherLogin). Seed__DemoData задаётся ЯВНО производной фикстурой.
///
/// Данные given кейсов — демо-сид спеки (<see cref="B17GroupsDemoDataFactory"/>:
/// Seed__DemoData=true точно воспроизводит src/client/app/mock/seed.ts — группы
/// ИК-221 (25 студентов), ИК-222 (5), ИК-223 (0), студенты student01..student32,
/// из них 31–32 без группы) либо пустое хранилище с сид-преподавателем
/// (базовая фикстура, Seed__DemoData=false — кейсы TS-126/TS-128/TS-202).
///
/// Изоляция сценариев: каждый тестовый КЛАСС со своей IClassFixture-фикстурой
/// получает свежий экземпляр приложения — чистые хранилища и лимитеры, состав
/// сида детерминирован (SeedRunner идемпотентен), поэтому занятость имён групп
/// given выполняется ровно.
///
/// BL-001 (среда прогона): DOTNET_USE_POLLING_FILE_WATCHER=true и
/// hostBuilder:reloadConfigOnChange=false — хосты параллельных батчей не
/// истощают inotify-лимит машины прогона; на проверяемые HTTP-контракты не
/// влияет.
/// </summary>
public class B17GroupsWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Демо-набор включён (true: ИК-221/222/223 + student01..32) или пустое хранилище.</summary>
    protected virtual bool SeedDemoData => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Хранилище приложения — PostgreSQL-only; тесты зоны работают на in-memory
        // двойках tests/TestInfrastructure (без ввода-вывода и внешней БД).
        builder.ConfigureServices(s => LabsApp.TestInfrastructure.InMemoryTestStorage.Register(s));
        // Явная строка подключения (не равна dev-умолчанию): Production-режим
        // тестовых хостов проходит StorageOptionsValidator; двойки хранилища
        // подключений не выполняют.
        builder.UseSetting("Storage:ConnectionString", "Host=127.0.0.1;Port=5434;Database=labsapp-test-double");
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");

        // Имя окружения строкой: константа Environments в compile-поверхности
        // тестового проекта неоднозначна между Hosting.Abstractions (aspnetcore 10
        // консолидировал сборки) — как в подпроекте B-08/Groups.
        builder.UseEnvironment("Development");
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        // При минимальном хостинге (WebApplicationBuilder) значения UseSetting
        // фабрики попадают в аргументы точки входа до запуска Program, поэтому
        // ключи передаются с разделителем ':' ('__'-иерархию создаёт только
        // провайдер переменных окружения).
        builder.UseSetting("Seed:DemoData", SeedDemoData ? "true" : "false");
    }
}

/// <summary>
/// Фикстура с демо-сидом — данные given кейсов TS-122, TS-123, TS-124, TS-125,
/// TS-127, TS-204 («Сид: ИК-221 (25 студентов), ИК-222 (5), ИК-223 (0)»,
/// «В ИК-222 есть студенты (сид 26–30)», «В группе ИК-221 25 студентов»).
/// </summary>
public sealed class B17GroupsDemoDataFactory : B17GroupsWebAppFactory
{
    protected override bool SeedDemoData => true;
}
