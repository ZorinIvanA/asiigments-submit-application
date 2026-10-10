using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B05.Infrastructure;

/// <summary>
/// Тестовый хост батча B-05 (зона tests/integration/B-05): собственная копия механики
/// фабрики (фабрика src/api/LabsApp.Tests.TestWebAppFactory — internal и чужая зона;
/// изоляция по образцу зон B-01/B-02, BL-001 BUG-001). Окружение — Development
/// (демо-сид включён по умолчанию, FR-004); фиксированные Auth__JwtKey и
/// Seed__TeacherPassword (ADR-010); content root — выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер переменных
/// окружения).
///
/// Изоляция сценариев лимитеров (TS-120/TS-154/TS-160/TS-161/TS-168/TS-169): счётчики
/// register/login/recovery живут в памяти процесса (FR-024, ADR-006), поэтому каждый
/// тестовый КЛАСС со своей IClassFixture-фикстурой получает свежий экземпляр
/// приложения и пустые счётчики («свежий экземпляр» в given кейсов). RemoteIpAddress
/// всех запросов через TestServer одинаков (единый in-memory канал) — условие кейсов
/// «с того же IP X» выполняется для любой последовательности запросов одного клиента.
/// </summary>
public sealed class B05WebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b05-integration-test-jwt-signing-key-0123456789abcdef";
    public const string TestTeacherPassword = "b05-test-teacher-password!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
