using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B18Profile.Infrastructure;

/// <summary>
/// Тестовый хост зоны B-18/backend (tests/integration/B-18/backend): собственная
/// копия механики фабрики (фабрики зон B-01..B-16 и src/api/LabsApp.Tests —
/// internal и чужие зоны; изоляция по образцу зон B-01/B-02/B-16, BL-001 BUG-001).
/// Окружение — Development. Аутентификации в приложении нет: все эндпоинты
/// публичны, «текущий пользователь» — засеянный преподаватель (Seed__TeacherLogin,
/// умолчание «teacher»); сценарии зоны ходят анонимным клиентом.
/// Seed__DemoData=false ЯВНО — кейсы батча не зависят от демо-набора
/// (AR-011/NFR-011): студенты и группы («student01 в ИК-221» и др.) создаются
/// прямым DI-сидом в own-фикстуре каждого тестового класса с дословно теми
/// значениями, что заданы кейсами. Content root — выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// Изоляция сценариев (хранилище in-memory — FR-002, ASM-007): каждый
/// тестовый КЛАСС со своей IClassFixture-фикстурой получает свежий экземпляр
/// приложения — пустые хранилища (кроме сид-преподавателя), поэтому выбранные
/// кейсами login/email свободны/заняты ровно так, как сказано в given.
/// </summary>
public class B18ProfileWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B18ProfileWebAppFactory()
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

        // Кейсы батча опираются только на собственных пользователей DI-сида:
        // демо-набор отключён ЯВНО — детерминизм независимо от умолчаний окружения
        // и быстрее старт (AR-011/NFR-011).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
