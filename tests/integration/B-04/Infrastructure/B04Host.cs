using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B04.Infrastructure;

/// <summary>
/// Базовый тестовый хост кейсов батча B-04 (FR-017/FR-020/FR-021/FR-022): собственная
/// копия механики фабрики (фабрики чужих зон и src/api/LabsApp.Tests — internal;
/// изоляция BL-001 BUG-001). Окружение — Development; Auth__JwtKey и
/// Seed__TeacherPassword зафиксированы; демо-набор задаётся подклассом ЯВНО
/// (детерминизм независимо от умолчаний окружения). Сессии — МИНТ access-JWT
/// через ITokenService хоста (ADR-015: доменные кейсы не зависят от POST
/// /auth/login), см. MintedSessions.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// попадают в аргументы точки входа до запуска Program, поэтому ключи передаются
/// с разделителем ':' ('__' иерархию создаёт только провайдер переменных окружения).
/// </summary>
public abstract class B04HostFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b04-labs-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    /// <summary>Демо-набор сида: false — хранилище наполняют тесты DI-сидом; true — «сид развёрнут».</summary>
    protected abstract bool DemoData { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Изоляция прогона: без file-watcher'ов конфигурации — на хосте с параллельными
        // батчами лимит inotify-инстансов ОС (128) исчерпывается, и хост не поднимается
        // (IOException в JsonConfigurationSource.Build); автотестам reload не нужен.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), SeedOptions.DefaultTeacherPassword);
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), DemoData ? "true" : "false");
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}

/// <summary>
/// Хост с пустым хранилищем работ/студентов (демо-набор выключен): CRUD-кейсы
/// FR-017 сами создают данные, поэтому «пара свободна» детерминирована.
/// </summary>
public sealed class B04LabsWebAppFactory : B04HostFactory
{
    protected override bool DemoData => false;
}

/// <summary>
/// Хост с развёрнутым сидом (демо-набор включён): 23 работы (семестр 1 №1–20,
/// семестр 2 №1–3) и студенты student01..student32 с ФИО «Иванов Иван Иванович NN» —
/// кейсы списка FR-017 и поиска FR-020, given «сид развёрнут».
/// </summary>
public sealed class B04DemoSeedWebAppFactory : B04HostFactory
{
    protected override bool DemoData => true;
}
