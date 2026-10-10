using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B01.Infrastructure;

/// <summary>
/// Тестовый хост батча B-01 (зона tests/integration/B-01): собственная копия
/// механики фабрики (фабрика из src/api/LabsApp.Tests — internal и чужая зона).
/// Умолчание — Development без переопределений (FR-001/FR-008: действуют умолчания
/// окружения; Auth__JwtKey в Development не обязателен — фабрика ключ не
/// подставляет, TS-051); для Production фабрика сама подставляет валидные секреты
/// (FR-008), отдельные сценарии переопределяют их через settings (поздние значения
/// выигрывают; пустая строка = «задана пустой», null-значение не допускается UseSetting).
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер переменных
/// окружения). Content root — выходной каталог тестов: wwwroot из TestAssets
/// копируется туда csproj-целью (проверки FR-002).
/// </summary>
public class B01WebAppFactory : WebApplicationFactory<Program>
{
    public const string ProductionJwtKey = "b01-integration-production-jwt-signing-key-0123456789abcdef";
    public const string ProductionTeacherPassword = "b01-production-teacher-password!";

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>
    /// Публичный конструктор по умолчанию: сценарии создают фабрики напрямую
    /// (new B01WebAppFactory() — не IClassFixture-фикстуры), этот — вариант
    /// Development без переопределений, умолчания конфигурации FR-001/FR-008.
    /// </summary>
    public B01WebAppFactory()
        : this(Environments.Development, null)
    {
    }

    /// <summary>
    /// Конструктор для отдельных сценариев и подклассов (окружение + settings).
    /// Не публичный: наружу выставлена только минимальная поверхность — сценарии
    /// без переопределений пользуются конструктором по умолчанию.
    /// </summary>
    internal B01WebAppFactory(string environment, IReadOnlyDictionary<string, string?>? settings)
    {
        _environment = environment;
        _settings = settings ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Короткоживущие тестовые хосты не перезагружают конфигурацию: reload-наблюдатели
        // appsettings расходуют inotify-экземпляры и исчерпывают лимит хоста при
        // параллельном старте (см. TestProcessDefaults).
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        if (string.Equals(_environment, Environments.Production, StringComparison.OrdinalIgnoreCase))
        {
            // FR-008: Production требует явные валидные секреты — задаём их по умолчанию;
            // сценарий может переопределить ключи ниже (settings применяются позднее).
            builder.UseSetting("Auth:JwtKey", ProductionJwtKey);
            builder.UseSetting("Seed:TeacherPassword", ProductionTeacherPassword);
        }

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(ToConfigKey(key), value);
        }
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
