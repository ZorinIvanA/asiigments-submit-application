using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>
/// База сценарных хостов recovery/reset-кейсов батча B-13 (TS-064..TS-078):
/// собственная копия механики фабрики (фабрики чужих зон B-01..B-12 и
/// src/api/LabsApp.Tests недоступны — BL-001 BUG-001; B13RecoveryWebAppFactory
/// зоны запечатан и не несёт инжектируемых часов/подмены IEmailSender),
/// тестовый log-sink (B13RecoveryLogSink — записи всех категорий) и инжектируемые
/// часы (FR-003/ADR-002: FakeTimeProvider заменяет TimeProvider из Program —
/// коллбэк ConfigureServices выполняется после Program, последняя регистрация
/// выигрывает).
///
/// Конфигурация — явные UseSetting на ключи с разделителем ':' (при минимальном
/// хостинге значения попадают в аргументы точки входа до запуска Program;
/// '__'-иерархию создаёт только провайдер переменных окружения). Content root —
/// выходной каталог тестов (wwwroot рядом с тестами отсутствует);
/// hostBuilder:reloadConfigOnChange=false — короткоживущие тестовые хосты не
/// перечитывают конфигурацию (урок зоны B-13: inotify-инстансы исчерпывают
/// пер-пользовательский лимит). Auth__Pbkdf2Iterations=1000 — тестовые умолчания
/// гейтов Δkdf (FR-027); Seed__DemoData=false — демо-набор отключён
/// (ADR-010/NFR-011), учётки кейсов — DI-сидом (B13RecoverySeed).
///
/// Подмена IEmailSender (опция ReplacesEmailSender) — стаб ТОЛЬКО внешней системы
/// доставки: спай-декоратор сохраняет прод-поведение dev-письма (TS-064), падающая
/// заглушка имитирует недоступный канал (TS-064, отдельный прогон). В Production-хосте
/// (TS-066) IEmailSender НЕ подменяется — наблюдается реальная композиция
/// ProductionEmailSender (no-op + один warning).
/// </summary>
public abstract class B13RecoveryScenarioHostFactory : WebApplicationFactory<Program>
{
    /// <summary>Итерации PBKDF2 тестовых хостов (умолчание гейтов Δkdf, FR-005).</summary>
    public const string TestPbkdf2Iterations = "1000";

    /// <summary>Log-sink тестового хоста (записи всех категорий без фильтров).</summary>
    public B13RecoveryLogSink LogSink { get; } = new();

    /// <summary>Единственный источник бизнес-времени тестового хоста (FR-003).</summary>
    public FakeTimeProvider Time { get; } = new();

    /// <summary>Имя окружения ASP.NET Core для хоста кейса.</summary>
    protected abstract string HostEnvironmentName { get; }

    /// <summary>Значение Auth__JwtKey хоста (Production-валидатор требует ≥32 символов).</summary>
    protected abstract string JwtKeyValue { get; }

    /// <summary>Значение Seed__TeacherPassword хоста (Production-guard требует нестандартный).</summary>
    protected abstract string TeacherPasswordValue { get; }

    /// <summary>Подменять ли регистрацию IEmailSender из композиция-корня.</summary>
    protected virtual bool ReplacesEmailSender => false;

    /// <summary>Реализация-замена IEmailSender (вызывается один раз при резолве).</summary>
    protected virtual IEmailSender CreateEmailSenderReplacement(IServiceProvider services) =>
        throw new InvalidOperationException(
            "ReplacesEmailSender=true обязан переопределить CreateEmailSenderReplacement.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironmentName);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");
        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), JwtKeyValue);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherLoginVariable), SeedOptions.DefaultTeacherLogin);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TeacherPasswordValue);
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
        builder.UseSetting(ToConfigKey(AuthOptions.Pbkdf2IterationsVariable), TestPbkdf2Iterations);
        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));
        builder.ConfigureServices(services =>
        {
            // Последняя регистрация TimeProvider выигрывает разрешение из DI (ADR-002).
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);

            if (ReplacesEmailSender)
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(CreateEmailSenderReplacement);
            }
        });
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}

/// <summary>
/// Development-хост recovery-кейсов с log-sink и спаем-счётчиком IEmailSender:
/// спай делегирует реальной DevEmailSender, поэтому запись 'EmailDev' с маркером
/// [DEV-EMAIL] появляется в log-sink как в основном прогоне, а кейс дополнительно
/// видит точное число вызовов отправки (TS-064: «IEmailSender вызван один раз»).
/// </summary>
public sealed class B13RecoveryDevSpyHost : B13RecoveryScenarioHostFactory
{
    /// <summary>Тестовый ключ подписи Development-хоста (явный — без warning эпизодического ключа).</summary>
    public const string TestJwtKey = "b13-recovery-dev-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    protected override string HostEnvironmentName => Environments.Development;

    protected override string JwtKeyValue => TestJwtKey;

    protected override string TeacherPasswordValue => SeedOptions.DefaultTeacherPassword;

    protected override bool ReplacesEmailSender => true;

    protected override IEmailSender CreateEmailSenderReplacement(IServiceProvider services) =>
        new B13CountingDevEmailSender(services.GetRequiredService<ILoggerFactory>());

    /// <summary>Спай IEmailSender (резолв материализует хост).</summary>
    public B13CountingDevEmailSender EmailSpy =>
        (B13CountingDevEmailSender)Services.GetRequiredService<IEmailSender>();
}

/// <summary>
/// Development-хост отдельного прогона TS-064: IEmailSender подменён падающей
/// заглушкой (канал доставки недоступен); log-sink и часы — как в основном прогоне.
/// </summary>
public sealed class B13RecoveryDevThrowingEmailHost : B13RecoveryScenarioHostFactory
{
    /// <summary>Тестовый ключ подписи Development-хоста (явный — без warning эпизодического ключа).</summary>
    public const string TestJwtKey = "b13-recovery-dev-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    /// <summary>Падающая заглушка доставки (экземпляр фабрики — доступ кейсу).</summary>
    public B13ThrowingEmailSender EmailSpy { get; } = new();

    protected override string HostEnvironmentName => Environments.Development;

    protected override string JwtKeyValue => TestJwtKey;

    protected override string TeacherPasswordValue => SeedOptions.DefaultTeacherPassword;

    protected override bool ReplacesEmailSender => true;

    protected override IEmailSender CreateEmailSenderReplacement(IServiceProvider services) => EmailSpy;
}

/// <summary>
/// Production-хост кейса TS-066 (конфигурация «Production»: заданы Auth__JwtKey
/// и нестандартный Seed__TeacherPassword — guard SeedOptionsValidator пройден):
/// IEmailSender НЕ подменяется — наблюдается реальная композиция
/// ProductionEmailSender (no-op + ровно один warning без адресата и содержимого).
/// </summary>
public sealed class B13RecoveryProdSecretsHost : B13RecoveryScenarioHostFactory
{
    /// <summary>Тестовый ключ подписи Production-стенда (≥ AuthOptions.JwtKeyMinLength).</summary>
    public const string TestJwtKey = "b13-recovery-prod-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    /// <summary>Нестандартный сид-пароль (удовлетворяет правилам пароля, ≠ умолчанию).</summary>
    public const string OwnTeacherPassword = "Ts066-B13-Teacher-Pass1!";

    protected override string HostEnvironmentName => Environments.Production;

    protected override string JwtKeyValue => TestJwtKey;

    protected override string TeacherPasswordValue => OwnTeacherPassword;
}

/// <summary>
/// Development-хост recovery/confirm- и reset-password-кейсов (TS-069..TS-078,
/// роль B11TimedWebAppFactory канонического прогона): log-sink и инжектируемые
/// часы есть, IEmailSender НЕ подменяется — реальная композиция dev-заглушки.
/// </summary>
public sealed class B13RecoveryTimedHost : B13RecoveryScenarioHostFactory
{
    /// <summary>Тестовый ключ подписи Development-хоста (явный — без warning эпизодического ключа).</summary>
    public const string TestJwtKey = "b13-recovery-timed-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    protected override string HostEnvironmentName => Environments.Development;

    protected override string JwtKeyValue => TestJwtKey;

    protected override string TeacherPasswordValue => SeedOptions.DefaultTeacherPassword;
}
