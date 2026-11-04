using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B12.Infrastructure;

/// <summary>
/// Тестовый хост с управляемым бизнес-временем для auth-кейсов батча B-12
/// (TS-060 «logout при истёкшем access», TS-061 «двойной logout», TS-066
/// «/auth/me с просроченным access»): FakeTimeProvider вместо системного
/// TimeProvider — композиция-корень единственная точка переопределения
/// (ADR-002 tech solution 5.0; образец — зона B-06, B06FakeTimeWebAppFactory).
/// Регистрация выполняется в ConfigureServices фабрики — ПОСЛЕ регистраций
/// Program, поэтому при разрешении TimeProvider побеждает последняя
/// регистрация (FakeTimeProvider); все TTL (exp access-JWT, expiresAt
/// refresh-токена) и проверки сроков читают его. Экземпляр <see cref="Time"/>
/// доступен тесту для перевода времени (Advance).
/// Прочие умолчания — как у <see cref="B12WebAppFactory"/>: Development,
/// фиксированный Auth__JwtKey, демо-набор отключён (Seed__DemoData=false).
/// </summary>
public sealed class B12FakeTimeWebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b12-faketime-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    /// <summary>Управляемое бизнес-время тестового хоста (перевод за exp access).</summary>
    public FakeTimeProvider Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Хранилище приложения — PostgreSQL-only; тесты зоны работают на in-memory
        // двойках tests/TestInfrastructure (без ввода-вывода и внешней БД).
        builder.ConfigureServices(s => LabsApp.TestInfrastructure.InMemoryTestStorage.Register(s));
        // Явная строка подключения (не равна dev-умолчанию): Production-режим
        // тестовых хостов проходит StorageOptionsValidator; двойки хранилища
        // подключений не выполняют.
        builder.UseSetting("Storage:ConnectionString", "Host=127.0.0.1;Port=5434;Database=labsapp-test-double");
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Вотчеры перечитывания конфигурации не нужны (как в зонах B-06/B-12):
        // пер-пользовательский лимит inotify в среде прогона ненадёжен.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), SeedOptions.DefaultTeacherPassword);

        // Явное отключение демо-набора — детерминизм независимо от умолчаний окружения.
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
