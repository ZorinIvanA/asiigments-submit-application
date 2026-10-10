using LabsApp.Hosting.Configuration;
using LabsApp.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B17.Infrastructure;

/// <summary>
/// Тестовый хост батча B-17 (зона tests/integration/B-17): собственная копия
/// механики фабрики (фабрики зон B-01..B-21 и src/api/LabsApp.Tests — internal
/// и чужие зоны; изоляция по образцу зон B-08/B-21, BL-001 BUG-001). Окружение
/// по умолчанию — Development (кейсы TS-068, TS-069, TS-070(а), TS-071, TS-072);
/// кейсы TS-070(б) и TS-190 используют <see cref="B17ProductionWebAppFactory"/>
/// (Production: заданы Auth__JwtKey и нестандартный Seed__TeacherPassword).
/// Seed__DemoData=false ЯВНО — кейсы батча не зависят от демо-набора; пользователи
/// создаются DI-сидом (ADR-015), POST /auth/login не используется. Content root —
/// выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// Log-sink (TS-068..TS-070, TS-190 — инспекция журнала по категориям): каждый
/// тестовый хост получает <see cref="B17LogSink"/>, собирающий записи всех
/// категорий; доступ — из свойства LogSink, изоляция — LogSink.Clear() перед
/// сценарием.
///
/// Счётная обёртка IEmailSender (TS-068: «IEmailSender вызван один раз»; точное
/// значение кода для поиска по журналу — TS-070/TS-190): регистрируется через
/// ConfigureTestServices — применяется ПОСЛЕ регистраций Program, замена
/// выигрывает; внутри обёртки — та же средоспецифичная реализация
/// (DevEmailSender/ProductionEmailSender), что выбирает композиция-корень, поэтому
/// записи письма в журнале создаёт реальный класс реализации (см. <see cref="B17EmailSpy"/>).
///
/// Изоляция сценариев (лимитер запросов кода 3/час на email_ci — FR-004/FR-012;
/// хранилище in-memory — FR-024): каждый тестовый КЛАСС со своей
/// IClassFixture-фикстурой получает свежий экземпляр приложения — пустые словари
/// лимитеров, пустое хранилище (кроме сид-преподавателя) и чистый sink, поэтому
/// выбранные кейсами email свободны/заняты ровно так, как сказано в given.
/// </summary>
public class B17WebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b17-integration-test-jwt-signing-key-0123456789abcdef";
    public const string TestTeacherPassword = "b17-test-teacher-password1!";

    /// <summary>Log-sink тестового хоста; изоляция — Clear() перед сценарием.</summary>
    public B17LogSink LogSink { get; } = new();

    /// <summary>Счётная обёртка IEmailSender тестового хоста (число вызовов + коды писем).</summary>
    public B17EmailSpy EmailSpy { get; } = new();

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B17WebAppFactory()
        : this(Environments.Development, new Dictionary<string, string?>())
    {
    }

    /// <summary>Внутренний конструктор для производных фикстур (окружение/настройки).</summary>
    protected B17WebAppFactory(string environment, IReadOnlyDictionary<string, string?> settings)
    {
        // Урок BL-001 (среда прогона): машина конвейера делит лимит
        // fs.inotify.max_user_instances (128) между параллельными батчами и
        // сессией оператора — создание экземпляра хоста падает на
        // PhysicalFilesWatcher, когда лимит исчерпан. Документированный
        // переключатель DOTNET_USE_POLLING_FILE_WATCHER переводит провайдеры
        // файлов хоста на опрос без inotify; на тестируемые HTTP-контракты
        // наблюдение за файлами конфигурации не влияет. Хост живёт в процессе
        // testhost — переменная процесса применяется ко всем провайдерам.
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");

        _environment = environment;
        _settings = settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);

        // Кейсы батча опираются только на собственных пользователей DI-сида:
        // демо-набор отключён ЯВНО — детерминизм независимо от умолчаний
        // окружения и быстрый старт (AR-011/NFR-011).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        // Log-sink в конвейере журналирования тестового хоста (TS-068..TS-070, TS-190).
        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));

        // Счётная обёртка IEmailSender: ConfigureTestServices применяется после
        // регистраций Program — регистрация-обёртка выигрывает. Внутри — та же
        // средоспецифичная реализация, что выбирает композиция-корень.
        var isDevelopment = string.Equals(_environment, Environments.Development, StringComparison.Ordinal);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEmailSender>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                EmailSpy.AttachInner(
                    isDevelopment
                        ? new DevEmailSender(loggerFactory)
                        : new ProductionEmailSender(loggerFactory));
                return EmailSpy;
            });
        });

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(ToConfigKey(key), value);
        }
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}

/// <summary>
/// Production-фикстура кейсов TS-070(б) и TS-190 (окружение Production,
/// «валидные секреты»): Auth__JwtKey задан явно (≥32 символа, не dev-ключ),
/// Seed__TeacherPassword задан явно, нестандартный (отличается от пароля
/// Development-фикстуры) и удовлетворяет правилам пароля. Seed__DemoData не
/// задаётся: в Production флаг всегда трактуется как false — проверяется самим
/// стартом хоста.
/// </summary>
public sealed class B17ProductionWebAppFactory : B17WebAppFactory
{
    public const string ProductionJwtKey = "b17-production-integration-jwt-signing-key-0123456789";
    public const string ProductionTeacherPassword = "b17-prod-teacher-password1!";

    public B17ProductionWebAppFactory()
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
