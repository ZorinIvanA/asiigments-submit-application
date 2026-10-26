using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B23.Infrastructure;

/// <summary>
/// Тестовый хост зоны B-23: РЕАЛЬНОЕ PostgreSQL-хранилище (Storage__Provider=
/// postgres + строка подключения к локальному кластеру из src/api/db/README.md).
/// БД обязана существовать: схема — src/api/db/02_schema.sql, демо-данные —
/// 03_seed_demo.sql. Сид приложения при старте хоста идемпотентно дубликатов
/// не создаёт. Каждый тест работает с собственными уникальными данными
/// (Guid-суффиксы) и удаляет их за собой — зона повторяемо гоняется на общей БД.
/// </summary>
public sealed class B23WebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Подключение к локальному кластеру разработки (db/README.md, вариант А).</summary>
    public const string ConnectionString =
        "Host=127.0.0.1;Port=5434;Database=labsapp;Username=labsapp;Password=labsapp";

    /// <summary>Демо-учётки SQL-сида (src/api/db/03_seed_demo.sql).</summary>
    public const string TeacherLogin = "teacher";

    public const string TeacherPassword = "teacher123!";

    public const string StudentLogin = "student01";

    public const string StudentPassword = "student123!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        // Явный выбор провайдера: postgres (провайдер БД — предмет зоны B-23).
        builder.UseSetting("Storage:Provider", "postgres");
        builder.UseSetting("Storage:ConnectionString", ConnectionString);
    }
}
