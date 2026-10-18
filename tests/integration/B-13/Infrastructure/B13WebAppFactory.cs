using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>
/// Тестовый хост батча B-13 (зона tests/integration/B-13): собственная копия механики
/// фабрики (фабрики зон B-01..B-11 и src/api/LabsApp.Tests — internal и чужие зоны;
/// изоляция по образцу зон B-01/B-04/B-10/B-11, BL-001 BUG-001). Окружение —
/// Development; Auth__JwtKey и Seed__TeacherPassword зафиксированы (ADR-010);
/// content root — выходной каталог тестов. Демо-сид выключен ЯВНО
/// (Seed__DemoData=false, AR-011/NFR-011: автотесты не зависят от демо-набора) —
/// все кейсы TS-148..TS-152 и TS-159..TS-162 наполняются DI-сидом с точными
/// количествами (7 студентов, 4 сдачи, лабы №1–3 семестра 1). Сессии тестов
/// создаются МИНТОМ access-JWT через ITokenService тестового хоста (ADR-022) —
/// без зависимости от POST /auth/login.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
/// </summary>
public sealed class B13WebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b13-integration-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Короткоживущие тестовые хосты не перезагружают конфигурацию (как в зоне
        // B-01 и в B13ScopeWebAppFactory): reloadOnChange создаёт inotify-инстансы,
        // исчерпание системного лимита валит построение хоста с IOException.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), SeedOptions.DefaultTeacherPassword);
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
