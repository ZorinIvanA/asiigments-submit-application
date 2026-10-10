using LabsApp.Hosting.Configuration;
using LabsApp.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B12.Infrastructure;

/// <summary>
/// База сценарных хостов recovery/reset-кейсов батча B-12 (TS-064..TS-078).
/// Собственная копия механики фабрики зоны (существующие B12RecoveryWebAppFactory —
/// sealed и без управления временем, B12FakeTimeWebAppFactory — без log-sink;
/// фабрики чужих зон и src/api/LabsApp.Tests недоступны — BL-001 BUG-001):
/// тестовый log-sink (записи всех категорий) и инжектируемые часы (FR-003/ADR-002:
/// FakeTimeProvider заменяет TimeProvider из Program — коллбэк ConfigureServices
/// выполняется после Program, последняя регистрация выигрывает). Время движется
/// ТОЛЬКО явно (Advance/SetUtcNow) — TTL кода 10 минут (FR-012) и reset-токена
/// 15 минут (IF-003) детерминированы.
///
/// Конфигурация — явные UseSetting на ключи с разделителем ':' (при минимальном
/// хостинге значения попадают в аргументы точки входа до запуска Program;
/// '__'-иерархию создаёт только провайдер переменных окружения). Content root —
/// выходной каталог тестов. Auth__Pbkdf2Iterations=1000 — тестовые умолчания
/// гейтов Δkdf (FR-027); Seed__DemoData=false — демо-набор FR-004 отключён
/// (ADR-010/NFR-011), учётки кейсов — DI-сидом (B12RecoveryStore).
///
/// Подмена IEmailSender (опция ReplacesEmailSender) — стаб ТОЛЬКО внешней системы
/// доставки: спай-декоратор сохраняет прод-поведение dev-письма (TS-064), падающая
/// заглушка имитирует недоступный канал (TS-064, отдельный прогон). В Production-хосте
/// (TS-066) IEmailSender НЕ подменяется — наблюдается реальная композиция
/// ProductionEmailSender (no-op + один warning).
/// </summary>
public abstract class B12RecoveryScenarioHost : WebApplicationFactory<Program>
{
    /// <summary>Log-sink тестового хоста (записи всех категорий без фильтров).</summary>
    public B12RecoveryLogSink LogSink { get; } = new();

    /// <summary>Единственный источник бизнес-времени тестового хоста (FR-003).</summary>
    public FakeTimeProvider Time { get; } = new();

    /// <summary>Итерации PBKDF2 тестовых хостов (FR-005/IF-002: тесты задают меньшее).</summary>
    public const string TestPbkdf2Iterations = "1000";

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

        // Перечитывание конфигурации не нужно (как у фабрик зоны B-12):
        // пер-пользовательский лимит inotify в среде прогона ненадёжен.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), JwtKeyValue);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherLoginVariable), SeedOptions.DefaultTeacherLogin);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TeacherPasswordValue);

        // Явное отключение демо-набора — детерминизм независимо от умолчаний окружения (FR-007).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        // Итерации PBKDF2 из конфигурации (FR-005/IF-002: тесты задают меньшее).
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
public sealed class B12RecoveryDevSpyHost : B12RecoveryScenarioHost
{
    /// <summary>Тестовый ключ подписи Development-хоста (явный — без warning эпизодического ключа).</summary>
    public const string TestJwtKey = "b12-recovery-dev-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    protected override string HostEnvironmentName => Environments.Development;

    protected override string JwtKeyValue => TestJwtKey;

    protected override string TeacherPasswordValue => SeedOptions.DefaultTeacherPassword;

    protected override bool ReplacesEmailSender => true;

    protected override IEmailSender CreateEmailSenderReplacement(IServiceProvider services) =>
        new B12CountingDevEmailSender(services.GetRequiredService<ILoggerFactory>());

    /// <summary>Спай IEmailSender (резолв материализует хост).</summary>
    public B12CountingDevEmailSender EmailSpy =>
        (B12CountingDevEmailSender)Services.GetRequiredService<IEmailSender>();
}

/// <summary>
/// Development-хост отдельного прогона TS-064: IEmailSender подменён падающей
/// заглушкой (канал доставки недоступен); log-sink и часы — как в основном прогоне.
/// </summary>
public sealed class B12RecoveryDevThrowingEmailHost : B12RecoveryScenarioHost
{
    /// <summary>Тестовый ключ подписи Development-хоста (явный — без warning эпизодического ключа).</summary>
    public const string TestJwtKey = "b12-recovery-dev-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    /// <summary>Падающая заглушка доставки (экземпляр фабрики — доступ кейсу).</summary>
    public B12ThrowingEmailSender EmailSpy { get; } = new();

    protected override string HostEnvironmentName => Environments.Development;

    protected override string JwtKeyValue => TestJwtKey;

    protected override string TeacherPasswordValue => SeedOptions.DefaultTeacherPassword;

    protected override bool ReplacesEmailSender => true;

    protected override IEmailSender CreateEmailSenderReplacement(IServiceProvider services) => EmailSpy;
}

/// <summary>
/// Production-хост кейса TS-066 (конфигурация «Production»: заданы Auth__JwtKey
/// и нестандартный Seed__TeacherPassword — guard SeedOptionsValidator/FR-006
/// пройден): IEmailSender НЕ подменяется — наблюдается реальная композиция
/// ProductionEmailSender (no-op + ровно один warning без адресата и содержимого).
/// </summary>
public sealed class B12RecoveryProdSecretsHost : B12RecoveryScenarioHost
{
    /// <summary>Тестовый ключ подписи Production-стенда (≥ AuthOptions.JwtKeyMinLength).</summary>
    public const string TestJwtKey = "b12-recovery-prod-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    /// <summary>Нестандартный сид-пароль (удовлетворяет правилам §8, ≠ умолчанию).</summary>
    public const string OwnTeacherPassword = "B12Ts066-Teacher-Pass1!";

    protected override string HostEnvironmentName => Environments.Production;

    protected override string JwtKeyValue => TestJwtKey;

    protected override string TeacherPasswordValue => OwnTeacherPassword;
}
