using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B08.Limiters.Infrastructure;

/// <summary>
/// Тестовый хост волны B-08 «лимитер/матрица» (кейсы TS-019..TS-022, TS-024,
/// TS-025): собственная копия механики фабрики (фабрики других зон и поддеревьев
/// — internal и чужие; изоляция по образцу B08WebAppFactory корня зоны и
/// B08AuthWebAppFactory поддерева Auth, BL-001 BUG-001). Окружение по умолчанию —
/// Development (матрица лимитов проверяется на dev-хосте, given кейсов TS-019/
/// TS-020/TS-024). Auth__JwtKey и Seed__TeacherPassword зафиксированы; сид —
/// учётка teacher/teacher123! (given TS-019: пользователь teacher существует);
/// Seed__DemoData=false ЯВНО — кейсы не зависят от демо-набора (AR-011/NFR-011).
/// Content root — выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// Изоляция сценариев (лимиты login 5/60с, register 5/3600с, recovery_request
/// 3/3600с — матрица FR-004; хранилище лимитера in-memory — IF-006): каждый
/// тестовый КЛАСС со своей IClassFixture-фикстурой получает свежий экземпляр
/// приложения — пустые лимитеры, пустое хранилище (кроме сид-преподавателя),
/// поэтому выбранные кейсами логины/email свободны ровно так, как сказано в given.
/// </summary>
public class B08LimitersWebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b08-limit-integration-test-jwt-signing-key-0123456789abcdef";
    public const string TestTeacherPassword = "b08-limit-test-teacher-password1!";

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B08LimitersWebAppFactory()
        : this(Environments.Development, new Dictionary<string, string?>())
    {
    }

    /// <summary>Внутренний конструктор для производных фикстур (окружение/настройки).</summary>
    protected B08LimitersWebAppFactory(string environment, IReadOnlyDictionary<string, string?> settings)
    {
        _environment = environment;
        _settings = settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Изоляция прогона: без file-watcher'ов конфигурации — на хосте с параллельными
        // батчами лимит inotify-инстансов ОС (128) исчерпывается, и тестовый хост не
        // поднимается (IOException в JsonConfigurationSource.Build); автотестам
        // reload конфигов не нужен (механика зон B-01..B-07).
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);

        // Кейсы волны опираются только на сид-преподавателя и собственных
        // пользователей, созданных регистрацией через публичный API: демо-набор
        // отключён ЯВНО — детерминизм независимо от умолчаний окружения (AR-011).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(ToConfigKey(key), value);
        }
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
