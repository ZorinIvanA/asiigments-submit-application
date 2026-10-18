using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B14.Infrastructure;

/// <summary>
/// Тестовый хост батча B-14 (зона tests/integration/B-14): собственная копия механики
/// фабрики (фабрики зон B-01..B-15 и src/api/LabsApp.Tests — internal и чужие зоны;
/// изоляция по образцу зон B-01/B-02, BL-001 BUG-001). Окружение — Development.
/// Auth__JwtKey и Seed__TeacherPassword зафиксированы харнесом (ADR-010);
/// Seed__DemoData=false ЯВНО — кейсы батча не зависят от демо-набора (AR-011/NFR-011):
/// пользователи создаются прямым DI-сидом в own-фикстуре каждого тестового класса
/// (арбитраж a-017/CR-001), преподаватель TS-196 берётся из сид-учётки (FR-004,
/// сидится всегда и идемпотентно, независимо от демо-набора). Content root —
/// выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// Изоляция сценариев (хранилище и лимитеры in-memory — FR-002/FR-080, ASM-007):
/// каждый тестовый КЛАСС со своей IClassFixture-фикстурой получает свежий экземпляр
/// приложения — пустые хранилища (кроме сид-преподавателя), пустые счётчики лимитеров,
/// поэтому заданные кейсами login/email свободны, а попытки входа укладываются в лимиты.
/// </summary>
public class B14WebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b14-integration-test-jwt-signing-key-0123456789abcdef";
    public const string TestTeacherPassword = "b14-test-teacher-password1!";

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B14WebAppFactory()
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);

        // Кейсы батча опираются только на сид-преподавателя (TS-196) и собственных
        // пользователей DI-сида: демо-набор отключён ЯВНО — детерминизм независимо
        // от умолчаний окружения и быстрее старт (AR-011/NFR-011).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
