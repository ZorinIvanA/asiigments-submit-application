using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B12.Infrastructure;

/// <summary>
/// Тестовый хост батча B-12 (зона tests/integration/B-12): собственная копия механики
/// фабрики (фабрики зон B-01..B-10 и src/api/LabsApp.Tests — internal и чужие зоны;
/// изоляция по образцу зон B-01/B-04/B-10, BL-001 BUG-001). Окружение — Development;
/// Auth__JwtKey и Seed__TeacherPassword зафиксированы (ADR-010); content root —
/// выходной каталог тестов. Демо-набор ОТКЛЮЧЁН ЯВНО (Seed__DemoData=false, AR-011/
/// NFR-011: автотесты не зависят от демо-сида): все шаги given кейсов TS-136..TS-147
/// наполняются DI-сидом (ADR-010) с точными полями кейса, а TS-141 требует точного
/// total=5 по хранилищу — демо-набор из 32 студентов сломал бы счёт.
///
/// Сессии тестов создаются МИНТОМ access-JWT через ITokenService тестового хоста
/// (ADR-022) — без зависимости от POST /auth/login.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
/// </summary>
public sealed class B12WebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b12-integration-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), SeedOptions.DefaultTeacherPassword);

        // Явное отключение демо-набора — детерминизм независимо от умолчаний окружения (FR-007).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
