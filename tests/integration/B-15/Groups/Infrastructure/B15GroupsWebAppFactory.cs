using LabsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B15.Groups.Infrastructure;

/// <summary>
/// Тестовый хост сценариев групп батча B-15 (зона tests/integration/B-15/Groups).
/// Окружение — Development; секреты Auth__JwtKey/Seed__TeacherPassword
/// зафиксированы, Auth__Pbkdf2Iterations=1000 — тестовое умолчание спеки
/// (FR-027: малые итерации KDF в тестовом хосте); Seed__DemoData задаётся ЯВНО
/// производной фикстурой.
///
/// Данные given кейсов — демо-сид спеки (Seed__DemoData=true воспроизводит
/// src/client/app/mock/seed.ts: группы ИК-221 (25 студентов), ИК-222 (5),
/// ИК-223 (0), студенты student01..student32, из них 31–32 без группы) — ровно
/// состояния given TS-102, TS-103 («группы ИК-224 нет»), TS-104 (существуют
/// группа G1=ИК-221 и «ИК-222»), TS-105 («в ИК-222 есть студенты»), TS-106/TS-160
/// («в группе 25 студентов»).
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
public class B15GroupsWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Ключ подписи JWT тестового хоста (FR-008: ≥32 символа).</summary>
    public const string TestJwtKey = "b15-groups-integration-test-jwt-signing-key-0123456789abcdef";

    /// <summary>Пароль сид-преподавателя фикстуры (значение непусто для хэширования сида).</summary>
    public const string TestTeacherPassword = "b15-groups-teacher-password1!";

    /// <summary>Демо-набор включён (true: ИК-221/222/223 + student01..32) или пустое хранилище.</summary>
    protected virtual bool SeedDemoData => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");

        // Имя окружения строкой: константа Environments в compile-поверхности
        // тестового проекта неоднозначна между Hosting.Abstractions (aspnetcore 10
        // консолидировал сборки) — как в подпроектах B-14/Groups, B-17/Groups и B-08/Groups.
        builder.UseEnvironment("Development");
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        // При минимальном хостинге (WebApplicationBuilder) значения UseSetting
        // фабрики попадают в аргументы точки входа до запуска Program, поэтому
        // ключи передаются с разделителем ':' ('__'-иерархию создаёт только
        // провайдер переменных окружения).
        builder.UseSetting("Auth:JwtKey", TestJwtKey);
        builder.UseSetting("Auth:Pbkdf2Iterations", "1000");
        builder.UseSetting("Seed:TeacherPassword", TestTeacherPassword);
        builder.UseSetting("Seed:DemoData", SeedDemoData ? "true" : "false");
    }
}

/// <summary>
/// Фикстура с демо-сидом — данные given кейсов TS-102, TS-103, TS-104, TS-105,
/// TS-106, TS-160 («Группы ИК-221 (25 студентов), ИК-222 (5), ИК-223 (0)»,
/// «группы ИК-224 нет», «Существует группа G1 и группа 'ИК-222'», «В ИК-222 есть
/// студенты», «В группе 25 студентов»).
/// </summary>
public sealed class B15GroupsDemoDataFactory : B15GroupsWebAppFactory
{
    protected override bool SeedDemoData => true;
}
