using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B18Profile.Infrastructure;

/// <summary>
/// Тестовый хост зоны B-18/backend (tests/integration/B-18/backend): собственная
/// копия механики фабрики (фабрики зон B-01..B-16 и src/api/LabsApp.Tests —
/// internal и чужие зоны; изоляция по образцу зон B-01/B-02/B-16, BL-001 BUG-001).
/// Окружение — Development.
/// Auth__JwtKey и Seed__TeacherPassword зафиксированы харнесом (ADR-010);
/// Seed__DemoData=false ЯВНО — кейсы батча не зависят от демо-набора (AR-011/NFR-011):
/// студенты и группы («student01 в ИК-221» и др.) создаются прямым DI-сидом в
/// own-фикстуре каждого тестового класса с дословно теми значениями, что заданы
/// кейсами. Auth__Pbkdf2Iterations=1000 — тестовое умолчание методики (FR-005:
/// «тесты могут задавать меньшее, например 1000»; tech solution, стек «тесты»):
/// Δkdf-кейсы TS-093/TS-094/TS-095 считают ОПЕРАЦИИ, а не время, и не зависят
/// от числа итераций. Ключ передаётся через ToConfigKey(AuthOptions.Pbkdf2IterationsVariable).
/// Content root — выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// Изоляция сценариев (хранилище и лимитеры in-memory — FR-002, ASM-007): каждый
/// тестовый КЛАСС со своей IClassFixture-фикстурой получает свежий экземпляр
/// приложения — пустые хранилища (кроме сид-преподавателя), пустые счётчики
/// лимитеров, поэтому выбранные кейсами login/email свободны/заняты ровно так,
/// как сказано в given.
/// </summary>
public class B18ProfileWebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b18-profile-integration-test-jwt-signing-key-0123456789abcdef";
    public const string TestTeacherPassword = "b18-test-teacher-password1!";

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B18ProfileWebAppFactory()
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);

        // Кейсы батча опираются только на собственных пользователей DI-сида:
        // демо-набор отключён ЯВНО — детерминизм независимо от умолчаний окружения
        // и быстрее старт (AR-011/NFR-011).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        // Тестовые итерации KDF (FR-005/tech solution; см. шапку класса).
        builder.UseSetting(ToConfigKey(AuthOptions.Pbkdf2IterationsVariable), "1000");
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
