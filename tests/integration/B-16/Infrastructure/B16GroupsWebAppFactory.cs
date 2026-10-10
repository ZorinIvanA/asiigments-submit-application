using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B16.Infrastructure;

/// <summary>
/// Тестовый хост сценариев групп батча B-16 (кейсы TS-102, TS-103, TS-104, TS-105,
/// TS-106, TS-160, TS-169; FR-019, FR-020). Собственная копия механики фабрики
/// (фабрики чужих зон и src/api/LabsApp.Tests — internal; изоляция зон — BL-001;
/// файл не трогает существующую фикс-стуру зоны B16WebAppFactory, у которой
/// Seed__DemoData жёстко false).
///
/// Окружение — Development; Auth__JwtKey и Seed__TeacherPassword зафиксированы;
/// Auth__Pbkdf2Iterations=1000 — тестовое умолчание спеки (FR-005/FR-027).
/// Seed__DemoData задаётся производной фикстурой:
///  - B16GroupsDemoDataFactory (true) — данные given кейсов TS-102, TS-103, TS-104,
///    TS-105, TS-106, TS-160: демо-сид спеки (SeedRunner воспроизводит
///    src/client/app/mock/seed.ts) — группы ИК-221 (25 студентов), ИК-222 (5),
///    ИК-223 (0), студенты student01..student32 (fullName «Иванов Иван Иванович NN»);
///  - базовый класс (false) — чистое хранилище (только сид-преподаватель): given
///    TS-169 «групп с пробельными именами не существует».
///
/// Изоляция сценариев (хранилище и лимитеры in-memory — FR-002): каждый тестовый
/// КЛАСС со своей IClassFixture-фикстурой получает свежий экземпляр приложения —
/// чистые хранилища и лимитеры (в том числе регистрационный 5/3600с: 2 запроса
/// TS-106 в свежей фикстуре не исчерпывают лимит; повторные прогоны тоже, ибо
/// состояние пересоздаётся — шов IRateLimitStore не требуется).
///
/// BL-001 (среда прогона): DOTNET_USE_POLLING_FILE_WATCHER=true и
/// hostBuilder:reloadConfigOnChange=false — хосты параллельных батчей не истощают
/// inotify-лимит машины прогона; на проверяемые HTTP-контракты не влияет.
/// </summary>
public class B16GroupsWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Ключ подписи JWT тестового хоста (FR-008: ≥32 символа).</summary>
    public const string TestJwtKey = "b16-groups-integration-test-jwt-signing-key-0123456789abcdef";

    /// <summary>Пароль сид-преподавателя фикстуры (значение непусто для хэширования сида).</summary>
    public const string TestTeacherPassword = "b16-groups-teacher-password1!";

    /// <summary>Демо-набор включён (ИК-221/222/223 + student01..32) или чистое хранилище.</summary>
    protected virtual bool SeedDemoData => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");

        // Имя окружения строкой: константа Environments в compile-поверхности
        // тестового проекта неоднозначна между Hosting.Abstractions (консолидация
        // aspnetcore 10) — как в зонах B-14/Groups, B-15/Groups, B-17/Groups.
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
/// TS-106, TS-160: «Группы ИК-221 (25 студентов), ИК-222 (5), ИК-223 (0)»,
/// «группы ИК-224/ИК-230 не существуют», «Существует группа G1 и группа "ИК-222"»,
/// «В ИК-222 есть студенты», «В группе ИК-221 25 сид-студентов», «в группе
/// 25 студентов (pageSize=10, полных страниц 3)».
/// </summary>
public sealed class B16GroupsDemoDataFactory : B16GroupsWebAppFactory
{
    protected override bool SeedDemoData => true;
}
