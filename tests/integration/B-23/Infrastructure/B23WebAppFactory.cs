using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B23.Infrastructure;

/// <summary>
/// Тестовый хост зоны B-23: РЕАЛЬНОЕ PostgreSQL-хранилище (строка подключения
/// к локальному кластеру из src/api/db/README.md). БД обязана существовать:
/// схема — src/api/db/02_schema.sql, демо-данные — 03_seed_demo.sql. Сид
/// приложения при старте хоста идемпотентно дубликатов не создаёт. Каждый тест
/// работает с собственными уникальными данными (Guid-суффиксы) и удаляет их
/// за собой — зона повторяемо гоняется на общей БД. Аутентификации в приложении
/// нет: POST /api/v1/auth/* удалён, «текущий пользователь» — засеянный
/// преподаватель (SQL-сид teacher); сценарии ходят анонимным клиентом.
/// </summary>
public sealed class B23WebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Подключение к локальному кластеру разработки (db/README.md, вариант А).</summary>
    public const string ConnectionString =
        "Host=127.0.0.1;Port=5434;Database=labsapp;Username=labsapp;Password=labsapp";

    /// <summary>Логины демо-учёток SQL-сида (src/api/db/03_seed_demo.sql);
    /// пароли роли не играют — аутентификации в приложении нет.</summary>
    public const string TeacherLogin = "teacher";

    public const string StudentLogin = "student01";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        // Зона B-23 — на РЕАЛЬНОМ PostgreSQL (провайдер приложения единственный);
        // отличие от остальных зон — явная строка подключения.
        builder.UseSetting("Storage:ConnectionString", ConnectionString);
    }
}
