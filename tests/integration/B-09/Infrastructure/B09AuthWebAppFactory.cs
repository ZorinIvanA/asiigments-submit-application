using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B09.Infrastructure;

/// <summary>
/// Семейство тестовых хостов зоны B-09 для матрицы Auth__JwtKey и инспекции
/// журнала (кейсы TS-051/TS-052/TS-151/TS-152): собственная копия механики
/// фабрики (фабрики других зон и базовая B09WebAppFactory фиксируют JwtKey
/// безусловно — для «ключ НЕ задан» нужна фабрика без этой настройки; чужие
/// фабрики internal, BL-001 BUG-001).
///
/// Базовые настройки (все кейсы): Auth__Pbkdf2Iterations=1000 (гейты Δkdf и
/// бенчмарк NFR-008 — FR-005/NFR-008), Seed__DemoData=false ЯВНО, отключение
/// файловых вотчеров конфигурации (пер-пользовательский лимит inotify —
/// образец B09WebAppFactory), log-sink <see cref="B09LogSink"/> в конвейере
/// журналирования (TS-052/TS-151).
///
/// Различия по кейсам — словарём настроек производных фикстур:
///  - <see cref="B09AuthDevFactory"/>: Development, Auth__JwtKey и
///    Seed__TeacherPassword заданы явно (TS-151, TS-152 Development);
///  - <see cref="B09AuthDevNoJwtKeyFactory"/>: Development, ключ и пароль сида
///    НЕ заданы — эпизодический ключ + warning (TS-052), сид-учётка по
///    умолчанию teacher/teacher123!;
///  - <see cref="B09AuthProductionFactory"/>: Production, валидные секреты —
///    Auth__JwtKey (≥32) и НЕстандартный Seed__TeacherPassword (TS-152
///    Production);
///  - <see cref="B09AuthProductionNoJwtKeyFactory"/>: Production, только
///    валидный Seed__TeacherPassword — единственная причина отказа старта
///    должна состоять в Auth__JwtKey (TS-051).
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения
/// UseSetting фабрики попадают в аргументы точки входа до запуска Program,
/// поэтому ключи передаются с разделителем ':' ('__'-иерархию создаёт только
/// провайдер переменных окружения).
/// </summary>
public class B09AuthWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Dev-ключ подписи JWT фикстур с ЯВНЫМ ключом (≥32 символа).</summary>
    public const string DevJwtKey = "b09-auth-integration-jwt-signing-key-0123456789abcdef";

    /// <summary>Dev-пароль сид-преподавателя фикстур с ЯВНЫМ паролем.</summary>
    public const string DevTeacherPassword = "b09-auth-teacher-password1!";

    /// <summary>Production-ключ подписи (≥32 символа; TS-152 — «задан»).</summary>
    public const string ProductionJwtKey = "b09-auth-production-integration-jwt-signing-key-0123456789";

    /// <summary>
    /// Production-пароль сид-преподавателя — НЕстандартный (умолчание
    /// teacher123! отвергается guard'ом валидации конфигурации).
    /// </summary>
    public const string ProductionTeacherPassword = "b09-auth-prod-teacher-password1!";

    /// <summary>Тестовые итерации KDF всех фикстур (FR-005: «тесты могут задавать меньшее, например 1000»).</summary>
    public const int TestPbkdf2Iterations = 1000;

    /// <summary>Log-sink тестового хоста; изоляция — Clear() перед сценарием.</summary>
    public B09LogSink LogSink { get; } = new();

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>Внутренний конструктор для производных фикстур (окружение/настройки).</summary>
    protected B09AuthWebAppFactory(string environment, IReadOnlyDictionary<string, string?> settings)
    {
        _environment = environment;
        _settings = settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Окружение прогона не предоставляет inotify-инстансы надёжно
        // (пер-пользовательский лимит 128 исчерпывается параллельными батчами):
        // файловые вотчеры перечитывания конфигурации тестовому хосту не нужны
        // (образец — B09WebAppFactory.ConfigureWebHost).
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        // Тестовые итерации KDF (FR-005/NFR-008); демо-набор отключён ЯВНО.
        builder.UseSetting(
            ToConfigKey(AuthOptions.Pbkdf2IterationsVariable),
            TestPbkdf2Iterations.ToString(CultureInfo.InvariantCulture));
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
/// Development-фикстура с ЯВНЫМИ Auth__JwtKey и Seed__TeacherPassword
/// (TS-151, TS-152 Development): ключ подписи фиксирован (детерминизм),
/// сид-пароль отличается от умолчания, чтобы кейсы не полагались на дефолты
/// окружения.
/// </summary>
public sealed class B09AuthDevFactory : B09AuthWebAppFactory
{
    public B09AuthDevFactory()
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
/// SeedOptions — login 'teacher', password 'teacher123!' (дословно given).
/// </summary>
public sealed class B09AuthDevNoJwtKeyFactory : B09AuthWebAppFactory
{
    public B09AuthDevNoJwtKeyFactory()
        : base(Environments.Development, new Dictionary<string, string?>())
    {
    }
}

/// <summary>
/// Production-фикстура с «валидными секретами» (TS-152, production-часть
/// матрицы NFR-007): Auth__JwtKey задан (≥32 символа), Seed__TeacherPassword
/// задан НЕстандартный и удовлетворяющий правилам пароля (валидация
/// конфигурации проходит). Seed__DemoData=false задаётся базой; в Production
/// флаг в любом случае трактуется как false.
/// </summary>
public sealed class B09AuthProductionFactory : B09AuthWebAppFactory
{
    public B09AuthProductionFactory()
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
/// валидный, поэтому валидация AuthOptions должна стать ЕДИНСТВЕННОЙ причиной
/// отказа старта — «Переменная Auth__JwtKey обязательна в окружении Production».
/// </summary>
public sealed class B09AuthProductionNoJwtKeyFactory : B09AuthWebAppFactory
{
    public B09AuthProductionNoJwtKeyFactory()
        : base(
            Environments.Production,
            new Dictionary<string, string?>
            {
                [SeedOptions.TeacherPasswordVariable] = ProductionTeacherPassword,
            })
    {
    }
}
