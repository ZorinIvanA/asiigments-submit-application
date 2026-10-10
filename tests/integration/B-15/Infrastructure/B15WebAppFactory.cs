using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B15.Infrastructure;

/// <summary>
/// Тестовый хост батча B-15 (зона tests/integration/B-15): собственная копия механики
/// фабрики (фабрики зон B-01..B-07 и src/api/LabsApp.Tests — internal и чужие зоны;
/// изоляция по образцу зон B-01/B-02, BL-001 BUG-001). Окружение по умолчанию —
/// Development (кейсы TS-089..TS-094, TS-193, TS-194); кейс TS-095 использует
/// <see cref="B15ProductionWebAppFactory"/> (окружение Production). Auth__JwtKey и
/// Seed__TeacherPassword зафиксированы харнесом (ADR-010); Seed__DemoData=false ЯВНО —
/// кейсы батча не зависят от демо-набора (AR-011/NFR-011), пользователи создаются
/// прямым DI-сидом репозиториев в own-фикстуре каждого тестового класса
/// (арбитраж a-017/CR-001: публичный API для сида не используется). Content root —
/// выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// Log-sink (кейсы TS-093/TS-094/TS-095 — инспекция журнала): каждый тестовый хост
/// получает <see cref="B15LogSink"/> (LogSink), собирающий записи всех категорий;
/// доступ — из свойства LogSink, изоляция — LogSink.Clear() перед сценарием.
///
/// Изоляция сценариев (хранилище in-memory): каждый тестовый КЛАСС со своей
/// IClassFixture-фикстурой получает свежий экземпляр приложения — пустые счётчики,
/// пустое хранилище (кроме сид-преподавателя) и чистый sink, поэтому выбранные
/// кейсами email свободны/заняты ровно так, как сказано в given.
/// </summary>
public class B15WebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b15-integration-test-jwt-signing-key-0123456789abcdef";
    public const string TestTeacherPassword = "b15-test-teacher-password1!";

    static B15WebAppFactory()
    {
        // Детерминизм на нагруженных хостах: наблюдение за appsettings.json/wwwroot
        // переводится на polling-режим — PhysicalFileProvider не создаёт экземпляры
        // inotify, лимит которых на машине прогона может быть исчерпан сторонними
        // процессами (IOException «configured user limit ... inotify instances»).
        // На поведение HTTP-сценариев батча перезагрузка конфигурации не влияет.
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
    }

    /// <summary>Log-sink тестового хоста; изоляция — Clear() перед сценарием.</summary>
    public B15LogSink LogSink { get; } = new();

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B15WebAppFactory()
        : this(Environments.Development, new Dictionary<string, string?>())
    {
    }

    /// <summary>Внутренний конструктор для производных фикстур (окружение/настройки).</summary>
    protected B15WebAppFactory(string environment, IReadOnlyDictionary<string, string?> settings)
    {
        _environment = environment;
        _settings = settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);

        // Кейсы батча опираются только на DI-сид собственных пользователей: демо-набор
        // отключён ЯВНО — детерминизм независимо от умолчаний окружения и быстрее старт
        // (AR-011/NFR-011; умолчание Development без явного 'false' — true, FR-007).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        // Log-sink в конвейере журналирования тестового хоста (инспекция TS-093/094/095).
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
/// Production-фикстура кейса TS-095 (окружение Production): Auth__JwtKey задан
/// харнесом явно (FR-030: ≥32 символа, не dev-ключ), Seed__TeacherPassword задан
/// явно и отличается от умолчания (FR-006: Production-guard валидирует правилами
/// §8 — пароль содержит буквы, цифры и спецзнак). Seed__DemoData не задаётся: в
/// Production флаг всегда трактуется как false (FR-007/ADR-007) — проверяется
/// самим стартом хоста. access-JWT кейса минтится ключом Auth__JwtKey ИМЕННО
/// этого хоста (через ITokenService из factory.Services).
/// </summary>
public sealed class B15ProductionWebAppFactory : B15WebAppFactory
{
    public const string ProductionJwtKey = "b15-production-integration-jwt-signing-key-0123456789";
    public const string ProductionTeacherPassword = "b15-prod-teacher-password1!";

    public B15ProductionWebAppFactory()
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
