using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Тестовый хост кейса TS-031: Development с FakeTimeProvider вместо системного
/// бизнес-времени (композиция-корень — единственная точка переопределения TimeProvider,
/// ADR-010). Регистрация выполняется в ConfigureServices фабрики — ПОСЛЕ регистраций
/// Program, поэтому при разрешении TimeProvider побеждает последняя регистрация
/// (FakeTimeProvider); все TTL (JWT exp, refresh) и окна лимитеров читают его.
/// Экземпляр <see cref="Time"/> доступен тесту для перевода времени (Advance).
/// </summary>
public sealed class B06FakeTimeWebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b06-faketime-test-jwt-signing-key-0123456789abcdef";

    public const string TestTeacherPassword = "b06-test-teacher-password1!";

    /// <summary>Управляемое бизнес-время тестового хоста (TS-031: AccessTtlMinutes+1 минута).</summary>
    public FakeTimeProvider Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // См. B06WebAppFactory: вотчеры перечитывания конфигурации не нужны —
        // пер-пользовательский лимит inotify в среде прогона ненадёжен.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
