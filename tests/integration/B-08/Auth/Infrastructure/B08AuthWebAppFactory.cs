using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B08.Auth.Infrastructure;

/// <summary>
/// Тестовый хост поддерева Auth/ зоны B-08 (кейсы TS-026..TS-029, TS-048..TS-052,
/// TS-151..TS-153, TS-157): собственная копия механики фабрики (фабрики других
/// зон и поддеревьев — internal и чужие; изоляция по образцу B08WebAppFactory
/// корня зоны и B08GroupsWebAppFactory, BL-001 BUG-001).
///
/// Базовые настройки (все кейсы): Auth__Pbkdf2Iterations=1000 (гейты Δkdf и
/// бенчмарк NFR-008 на тестовых итерациях — FR-005/NFR-008), Seed__DemoData=false
/// ЯВНО (кейсы не зависят от демо-набора, AR-011/NFR-011; сид-преподаватель при
/// этом засеивается всегда). Log-sink <see cref="B08AuthLogSink"/> подключён
/// каждому хосту (кейсы TS-052, TS-151 — инспекция журнала).
///
/// Различия по кейсам — в производных фикстурах через словарь настроек:
///  - <see cref="B08AuthDevFactory"/>: Development, Auth__JwtKey и
///    Seed__TeacherPassword заданы явно (TS-026..TS-029, TS-048..TS-050,
///    TS-151..TS-153, TS-157);
///  - <see cref="B08AuthDevNoJwtKeyFactory"/>: Development, Auth__JwtKey и
///    Seed__TeacherPassword НЕ заданы — эпизодический ключ + warning (TS-052),
///    сид-учётка по умолчанию teacher/teacher123! (умолчания SeedOptions);
///  - <see cref="B08AuthProductionFactory"/>: Production, «валидные секреты» —
///    Auth__JwtKey (≥32) и НЕстандартный Seed__TeacherPassword (TS-152,
///    production-часть матрицы Set-Cookie);
///  - <see cref="B08AuthProductionNoJwtKeyFactory"/>: Production, Auth__JwtKey
///    НЕ задан при валидном Seed__TeacherPassword — единственная причина
///    отказа старта должна состоять в ключе подписи (TS-051).
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// «В рамках одного процесса теста конфигурация изменена» (кейсы TS-026/TS-027):
/// Auth__Pbkdf2Iterations читается хэшером из IOptions&lt;AuthOptions&gt; на КАЖДЫЙ
/// вызов Hash (IF-002), поэтому смена выполняется мутацией
/// OptionsManager.Value из DI — хранилище не пересоздаётся, хост не
/// перезапускается (дословно given кейса).
/// </summary>
public class B08AuthWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Dev-ключ подписи JWT фикстур с ЯВНЫМ ключом (≥32 символа).</summary>
    public const string DevJwtKey = "b08-auth-integration-jwt-signing-key-0123456789abcdef";

    /// <summary>Dev-пароль сид-преподавателя фикстур с ЯВНЫМ паролем.</summary>
    public const string DevTeacherPassword = "b08-auth-teacher-password1!";

    /// <summary>Production-ключ подписи (≥32 символа; TS-152 — «задан»).</summary>
    public const string ProductionJwtKey = "b08-auth-production-integration-jwt-signing-key-0123456789";

    /// <summary>Production-пароль сид-преподавателя — НЕстандартный (умолчание teacher123! отвергается guard'ом).</summary>
    public const string ProductionTeacherPassword = "b08-auth-prod-teacher-password1!";

    /// <summary>Тестовые итерации KDF всех фикстур (FR-005: «тесты могут задавать меньшее, например 1000»).</summary>
    public const int TestPbkdf2Iterations = 1000;

    /// <summary>Log-sink тестового хоста; изоляция — Clear() перед сценарием.</summary>
    public B08AuthLogSink LogSink { get; } = new();

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>Внутренний конструктор для производных фикстур (окружение/настройки).</summary>
    protected B08AuthWebAppFactory(string environment, IReadOnlyDictionary<string, string?> settings)
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

        // Тестовые итерации KDF (FR-005/NFR-008) и отключение демо-набора ЯВНО
        // (AR-011/NFR-011); сид-преподаватель засеивается при любом значении.
        builder.UseSetting(ToConfigKey(AuthOptions.Pbkdf2IterationsVariable), TestPbkdf2Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        // Log-sink в конвейере журналирования тестового хоста (TS-052, TS-151).
        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(ToConfigKey(key), value);
        }
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}

/// <summary>
/// Development-фикстура с ЯВНЫМИ Auth__JwtKey и Seed__TeacherPassword — базовые
/// кейсы батча (TS-026..TS-029, TS-048..TS-050, TS-151..TS-153, TS-157): ключ
/// подписи фиксирован (детерминизм), сид-пароль отличается от умолчания, чтобы
/// кейсы, где вход выполняет сид-учётка, не полагались на дефолты окружения.
/// </summary>
public sealed class B08AuthDevFactory : B08AuthWebAppFactory
{
    public B08AuthDevFactory()
        : base(
            Environments.Development,
            new Dictionary<string, string?>
            {
                [AuthOptions.JwtKeyVariable] = DevJwtKey,
                [SeedOptions.TeacherPasswordVariable] = DevTeacherPassword,
            })
    {
    }
}

/// <summary>
/// Development-фикстура БЕЗ Auth__JwtKey и БЕЗ Seed__TeacherPassword (TS-052):
/// композиция-корень подставляет эпизодический случайный ключ и пишет warning
/// в «Hosting.Configuration» (FR-008); сид-учётка получает умолчания
/// SeedOptions — login 'teacher', password 'teacher123!' (дословно given кейса).
/// </summary>
public sealed class B08AuthDevNoJwtKeyFactory : B08AuthWebAppFactory
{
    public B08AuthDevNoJwtKeyFactory()
        : base(Environments.Development, new Dictionary<string, string?>())
    {
    }
}

/// <summary>
/// Production-фикстура с «валидными секретами» (TS-152, production-часть
/// матрицы NFR-007): Auth__JwtKey задан (≥32 символа), Seed__TeacherPassword
/// задан НЕстандартный и удовлетворяющий правилам §8 (guard'ы FR-006
/// AuthOptionsValidator/SeedOptionsValidator проходят). Seed__DemoData не
/// задаётся: в Production флаг всегда трактуется как false.
/// </summary>
public sealed class B08AuthProductionFactory : B08AuthWebAppFactory
{
    public B08AuthProductionFactory()
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

/// <summary>
/// Production-фикстура БЕЗ Auth__JwtKey (TS-051): Seed__TeacherPassword задан
/// валидный, поэтому AuthOptionsValidator должна стать ЕДИНСТВЕННОЙ причиной
/// отказа старта — «Переменная Auth__JwtKey обязательна в окружении Production».
/// </summary>
public sealed class B08AuthProductionNoJwtKeyFactory : B08AuthWebAppFactory
{
    public B08AuthProductionNoJwtKeyFactory()
        : base(
            Environments.Production,
            new Dictionary<string, string?>
            {
                [SeedOptions.TeacherPasswordVariable] = ProductionTeacherPassword,
            })
    {
    }
}
