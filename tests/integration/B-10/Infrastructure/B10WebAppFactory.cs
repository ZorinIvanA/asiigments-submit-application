using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>
/// Тестовый хост батча B-10 (зона tests/integration/B-10): собственная копия механики
/// фабрики (фабрики зон B-01..B-07 и src/api/LabsApp.Tests — internal и чужие зоны;
/// изоляция по образцу зон B-01/B-04, BL-001 BUG-001). Окружение — Development;
/// Auth__JwtKey и Seed__TeacherPassword зафиксированы (ADR-010); content root —
/// выходной каталог тестов. Демо-набор FR-004 отключён ЯВНО (умолчание харнесов
/// Seed__DemoData=false, ADR-010: FR-007/AR-011 запрещают автотестам зависеть от
/// демо-сида — NFR-011): учётка teacher создаётся сидом из Seed__* идемпотентно
/// независимо от флага демо-набора (SeedRunner), лабы/студенты/группы/сдачи —
/// DI-сидом кейса (B10Seed, CR-001). Сессии тестов создаются МИНТОМ access-JWT
/// через ITokenService тестового хоста (ADR-022) — без зависимости от
/// POST /auth/login, отсутствующего в волне доменных задач (ISS-002).
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
/// </summary>
public abstract class B10HostFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b10-integration-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    /// <summary>Включён ли демонстрационный набор FR-004 в сиде хоста (у хостов батча — false).</summary>
    protected abstract bool DemoData { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), SeedOptions.DefaultTeacherPassword);
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), DemoData ? "true" : "false");
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}

/// <summary>
/// Хост батча B-10 без демо-набора (Seed__DemoData=false — ADR-010/NFR-011): в сиде
/// только учётка teacher из Seed__*; данные шагов given — DI-сид кейса (B10Seed).
/// Кейсов, целенаправленно проверяющих сам демо-набор FR-004, в батче нет (CR-001),
/// поэтому отдельный демо-хост из зоны удалён.
/// </summary>
public sealed class B10NoDemoWebAppFactory : B10HostFactory
{
    protected override bool DemoData => false;
}
