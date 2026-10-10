using LabsApp.Hosting.Configuration;
using LabsApp.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B15.Infrastructure;

/// <summary>
/// Тестовый хост кейсов восстановления/сброса пароля батча B-15 (TS-083, TS-084,
/// TS-085, TS-207 — Development; TS-190 — <see cref="B15RecoveryProductionWebAppFactory"/>).
/// Наследник фабрики зоны B15WebAppFactory (Development, Auth__JwtKey,
/// Seed__TeacherPassword, Seed__DemoData=false, log-sink B15LogSink — общая
/// механика зоны; файл B15WebAppFactory.cs не модифицируется). Добавки под кейсы
/// восстановления:
///  - Auth__Pbkdf2Iterations=1000 (методика тестовых фабрик, IF-002 — итерации
///    читаются из конфигурации; сид/Verify кейсов не тянут 210000 дериваций);
///  - счётная обёртка IEmailSender (<see cref="B15EmailSpy"/>) через
///    ConfigureTestServices — применяется ПОСЛЕ регистраций Program, замена
///    выигрывает; внутри — та же средоспецифичная реализация (DevEmailSender/
///    ProductionEmailSender), что выбирает композиция-корень, поэтому записи
///    письма в журнале создаёт реальный класс реализации.
/// Изоляция сценариев (лимитер recovery_request 3/час на email, хранилище
/// in-memory): каждый тестовый КЛАСС со своей IClassFixture-фикстурой получает
/// свежий хост — пустые хранилища и лимитеры; «живой код» добывается собственным
/// POST /auth/recovery/request в пределах кейса.
/// </summary>
public class B15RecoveryWebAppFactory : B15WebAppFactory
{
    /// <summary>Счётная обёртка IEmailSender тестового хоста (письма с кодами).</summary>
    public B15EmailSpy EmailSpy { get; } = new();

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B15RecoveryWebAppFactory()
    {
    }

    /// <summary>Внутренний конструктор для производных фикстур (окружение/настройки).</summary>
    protected B15RecoveryWebAppFactory(string environment, IReadOnlyDictionary<string, string?> settings)
        : base(environment, settings)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Базовая конфигурация зоны (Development, ключи, сид, log-sink) — затем добавки.
        base.ConfigureWebHost(builder);

        // Итерации PBKDF2 из конфигурации (IF-002; тестовое значение — 1000).
        builder.UseSetting(ToConfigKey(AuthOptions.Pbkdf2IterationsVariable), "1000");

        // Счётная обёртка IEmailSender: выбор внутренней реализации — по
        // IHostEnvironment, как в композиция-корне (IF-005).
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sp =>
            {
                var environment = sp.GetRequiredService<IHostEnvironment>();
                var loggerFactory = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();
                EmailSpy.AttachInner(
                    environment.IsDevelopment()
                        ? new DevEmailSender(loggerFactory)
                        : new ProductionEmailSender(loggerFactory));
                return EmailSpy;
            });
        });
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}

/// <summary>
/// Production-фикстура кейса TS-190 (окружение Production, «валидные секреты»):
/// Auth__JwtKey задан явно (≥32 символа, не dev-ключ), Seed__TeacherPassword
/// задан явно, нестандартный (отличается от Development-фикстур зоны) и
/// удовлетворяет правилам пароля (Production-guard, FR-006). Seed__DemoData
/// наследуется от базовой фабрики (явное 'false'). IEmailSender внутри обёртки —
/// ProductionEmailSender: no-op + один warning без адресата и содержимого.
/// </summary>
public sealed class B15RecoveryProductionWebAppFactory : B15RecoveryWebAppFactory
{
    public const string ProductionJwtKey = "b15-recovery-production-jwt-signing-key-0123456789abcdef";
    public const string ProductionTeacherPassword = "b15-recovery-prod-teacher1!";

    public B15RecoveryProductionWebAppFactory()
        : base(
            Environments.Production,
            new Dictionary<string, string?>
            {
                [AuthOptions.JwtKeyVariable] = ProductionJwtKey,
                [SeedOptions.TeacherPasswordVariable] = ProductionTeacherPassword,
            })
    {
    }
}
