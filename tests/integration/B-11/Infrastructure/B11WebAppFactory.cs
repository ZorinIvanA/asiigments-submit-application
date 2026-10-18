using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B11.Infrastructure;

/// <summary>
/// Тестовый хост батча B-11 (зона tests/integration/B-11): собственная копия механики
/// фабрики (фабрики чужих зон и src/api/LabsApp.Tests — internal; изоляция по образцу
/// зон B-01/B-04/B-10, урок BL-001 BUG-001). Окружение — Development; Auth__JwtKey,
/// Seed__TeacherLogin/Seed__TeacherPassword и Seed__DemoData=false зафиксированы
/// (ADR-010); content root — выходной каталог тестов.
///
/// given кейсов входа «пользователь teacher/teacher123! существует (сид)» исполняется
/// сидом FR-006 (учётка преподавателя сеется при построении приложения независимо от
/// демо-набора; демо-студенты выключены — ни один кейс батча на них не опирается).
///
/// Auth:Pbkdf2Iterations=1000 — тестовые умолчания гейтов Δkdf (FR-027, tech solution
/// «тесты»): гейт «ровно одна деривация» не должен стоить секунд реального CPU. Ключ
/// задаётся строкой (константа Auth__Pbkdf2Iterations появится в LabsApp с реворком
/// Api.Auth.Core) — зона собирается и до приземления реворка.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер переменных
/// окружения).
/// </summary>
public class B11WebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b11-integration-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    /// <summary>Тестовые итерации PBKDF2 (FR-027): гейты Δkdf исполняются быстро.</summary>
    public const string TestPbkdf2Iterations = "1000";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherLoginVariable), SeedOptions.DefaultTeacherLogin);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), SeedOptions.DefaultTeacherPassword);
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
        builder.UseSetting("Auth:Pbkdf2Iterations", TestPbkdf2Iterations);
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
