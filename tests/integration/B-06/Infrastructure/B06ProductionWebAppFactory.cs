using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Production-фикстура кейса TS-030 (NFR-010 «Secure — в Production»): Production-хост
/// с ВАЛИДНЫМИ секретами (given кейса). Auth__JwtKey задан явно — не менее 32 символов
/// и не dev-ключ (FR-030 guard на старте); Seed__TeacherPassword задан явно, отличается
/// от умолчания и удовлетворяет правилам §8 (FR-006 guard). Seed__DemoData не задаётся:
/// в Production флаг ВСЕГДА трактуется как false (FR-007) — проверяется самим стартом
/// хоста. Пользователь с известным паролем — сид-преподаватель (Seed__TeacherLogin,
/// умолчание «teacher»; DI-сид, ADR-010).
/// </summary>
public sealed class B06ProductionWebAppFactory : WebApplicationFactory<Program>
{
    public const string ProductionJwtKey = "b06-production-integration-jwt-signing-key-0123456789";

    public const string ProductionTeacherPassword = "b06-prod-teacher-password1!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // См. B06WebAppFactory: вотчеры перечитывания конфигурации не нужны —
        // пер-пользовательский лимит inotify в среде прогона ненадёжен.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), ProductionJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), ProductionTeacherPassword);
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
