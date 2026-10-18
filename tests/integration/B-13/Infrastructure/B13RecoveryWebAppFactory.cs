using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>
/// Тестовый хост recovery-кейсов батча B-13 (TS-162/TS-163). Собственная копия
/// механики фабрики (B13WebAppFactory запечатан и не несёт log-sink; фабрики
/// чужих зон B-01..B-12 и src/api/LabsApp.Tests недоступны — BL-001 BUG-001;
/// образец — B13WebAppFactory зоны, наследующий WebApplicationFactory&lt;Program&gt;
/// напрямую с копией умолчаний): Development; Auth__JwtKey и Seed__TeacherPassword
/// зафиксированы (ADR-010); content root — выходной каталог тестов; демо-набор
/// ОТКЛЮЧЁН ЯВНО (Seed__DemoData=false, AR-011/NFR-011). Добавки под кейсы
/// восстановления:
///  - B13RecoveryLogSink в конвейере журналирования — given TS-162 «живой код C,
///    значение известно тесту из записи 'EmailDev'»: код извлекается из
///    [DEV-EMAIL]-записи (IF-005/ADR-012);
///  - Auth__Pbkdf2Iterations=1000 (умолчание тестовых фабрик, FR-005/ASM-004
///    «тесты задают меньшее») — ветки входа TS-163 (старый/новый пароль) быстры,
///    а Δkdf(reset_password)=1 считается ПО МЕТКАМ, не по итерациям.
///
/// Бизнес-время не переводится (переводы за TTL кейсам TS-162/TS-163 не нужны) —
/// системный TimeProvider хоста; код (TTL 10 мин) и reset-токен (TTL 15 мин)
/// расходуются в пределах секунд прогона.
/// Изоляция сценариев: каждый тестовый КЛАСС со своей IClassFixture-фикстурой
/// получает свежий хост — пустые хранилища; лимит recovery_request (3/час,
/// FR-004) в пределах кейса задействуется одним запросом — «не исчерпан».
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
/// </summary>
public sealed class B13RecoveryWebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b13-recovery-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    /// <summary>Log-sink хоста: извлечение [DEV-EMAIL]-записей (IF-005).</summary>
    public B13RecoveryLogSink LogSink { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Перечитывание конфигурации не нужно (как у B13WebAppFactory зоны):
        // пер-пользовательский лимит inotify в среде прогона ненадёжен.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), SeedOptions.DefaultTeacherPassword);

        // Явное отключение демо-набора — детерминизм независимо от умолчаний окружения (FR-007).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        // Итерации PBKDF2 из конфигурации (FR-005/IF-002: тесты задают меньшее).
        builder.UseSetting(ToConfigKey(AuthOptions.Pbkdf2IterationsVariable), "1000");

        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
