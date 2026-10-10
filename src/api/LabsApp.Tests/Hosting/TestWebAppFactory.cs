using System.Globalization;
using System.Net;
using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.Tests.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabsApp.Tests.Hosting;

/// <summary>
/// Базовая фабрика тестового хоста (ADR-010): явное окружение (по умолчанию Development),
/// фиксированные Auth__JwtKey и Seed__TeacherPassword, content root — выходной каталог
/// тестов (wwwroot из TestAssets копируется туда csproj-целью). Отдельные сценарии
/// переопределяют переменные через settings — поздние значения выигрывают.
///
/// Умолчания тестовой инфраструктуры (T-006, FR-027): каждый хост по умолчанию
/// стартует с Auth__Pbkdf2Iterations=1000 (малые итерации KDF) и Seed__DemoData=false
/// (доменные данные задаются DI-сидом, <see cref="TestSession"/>). Тестам,
/// проверяющим умолчания ПРИЛОЖЕНИЯ (композиция-корень, окружение), харнес-умолчания
/// отключаются внутренним конструктором (infrastructureDefaults: false).
///
/// Швы (FR-027):
/// — KDF: <see cref="KdfSnapshot"/>/<see cref="KdfDelta"/> — снимки
///   IKdfCounter.Snapshot() из factory.Services и Δ по меткам вызывателя (IF-002);
/// — IP: <see cref="SetClientIp"/> — RemoteIpAddress соединения подставляется
///   middleware (<see cref="RemoteIpSubstitutionFilter"/>, образец зон B-03/B-06)
///   из заголовка <see cref="RemoteIpHeader"/>; TestServer не заполняет
///   RemoteIpAddress, запросы без заголовка остаются без изменений (ADR-006).
///
/// Log-sink (ADR-018, T-004 — единственный владелец): каждый тестовый хост получает
/// TestLogSink (LogSink), собирающий записи всех категорий; доступ к записям — из
/// свойства LogSink, изоляция тестов — LogSink.Clear().
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' (иерархия разделов; '__' для in-memory/командной
/// строки иерархию не создаёт — так работает только провайдер переменных окружения).
/// </summary>
public sealed class TestWebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "test-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    // Пароль обязан проходить правила §8 (FR-006: Production-guard валидирует
    // Seed__TeacherPassword единым валидатором) — прежнее значение без цифры
    // стало бы отказом старта Production-хостов.
    public const string TestTeacherPassword = "test-teacher-password1!";

    /// <summary>Умолчание Auth__Pbkdf2Iterations тестовых хостов (FR-027: малые итерации KDF).</summary>
    public const int TestPbkdf2Iterations = 1000;

    /// <summary>Заголовок тестовой подмены RemoteIpAddress соединения (IP-шов, ADR-006).</summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    /// <summary>Log-sink тестового хоста; изоляция тестов — Clear() в конструкторе тестового класса.</summary>
    public TestLogSink LogSink { get; } = new();

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly bool _infrastructureDefaults;

    /// <summary>Единственный публичный конструктор — для IClassFixture (xunit создаёт фикстуру без аргументов).</summary>
    public TestWebAppFactory()
        : this(null, null)
    {
    }

    internal TestWebAppFactory(
        string? environment = null,
        IReadOnlyDictionary<string, string?>? settings = null,
        bool infrastructureDefaults = true)
    {
        _environment = environment ?? Environments.Development;
        _settings = settings ?? new Dictionary<string, string?>();
        _infrastructureDefaults = infrastructureDefaults;
    }

    // ------------------------------------------------------------------
    // Шов KDF (IF-002, FR-027): снимки счётчика дериваций и Δ между ними
    // ------------------------------------------------------------------

    /// <summary>
    /// Снимок IKdfCounter.Snapshot() из factory.Services (метка вызывателя →
    /// число дериваций с момента старта хоста) — тестовый шов гейтов Δkdf.
    /// </summary>
    public IReadOnlyDictionary<string, long> KdfSnapshot() =>
        Services.GetRequiredService<IKdfCounter>().Snapshot();

    /// <summary>
    /// Δ по метке вызывателя между двумя снимками (<see cref="KdfSnapshot"/>):
    /// отсутствие метки в снимке трактуется как 0 (нечитаемая метка — не ошибка).
    /// </summary>
    public static long KdfDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after,
        string caller)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentException.ThrowIfNullOrEmpty(caller);

        return after.GetValueOrDefault(caller) - before.GetValueOrDefault(caller);
    }

    // ------------------------------------------------------------------
    // Шов IP (IF-006, ADR-006): RemoteIpAddress соединения тестового клиента
    // ------------------------------------------------------------------

    /// <summary>
    /// Фиксирует RemoteIpAddress соединения для ВСЕХ запросов клиента: заголовок
    /// <see cref="RemoteIpHeader"/> конвейер хоста переводит в
    /// context.Connection.RemoteIpAddress (см. шапку класса). null — снять
    /// подмену. Строка, не разбираемая как IP-адрес, — ArgumentException
    /// (упавший на настройке тест лучше тихо неверного ключа лимитера).
    /// </summary>
    public static void SetClientIp(HttpClient client, string? ipAddress)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (ipAddress is not null && !IPAddress.TryParse(ipAddress, out _))
        {
            throw new ArgumentException(
                $"Строка '{ipAddress}' не разбирается как IP-адрес (ожидался RemoteIpAddress соединения).",
                nameof(ipAddress));
        }

        client.DefaultRequestHeaders.Remove(RemoteIpHeader);
        if (ipAddress is not null)
        {
            client.DefaultRequestHeaders.Add(RemoteIpHeader, ipAddress);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Умолчания тестовой инфраструктуры (T-006): малые итерации KDF и явное
        // отключение демо-набора; scenario-настройки ниже их переопределяют.
        if (_infrastructureDefaults)
        {
            builder.UseSetting(
                ToConfigKey(AuthOptions.Pbkdf2IterationsVariable),
                TestPbkdf2Iterations.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
        }

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);

        // ADR-018: log-sink в конвейере журналирования тестового хоста. Провайдер
        // сам не фильтрует — в sink попадают все записи, пропущенные правилами
        // уровня хоста (по умолчанию Information и выше для всех категорий).
        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));

        // IP-шов: подмена RemoteIpAddress соединения до всего конвейера приложения
        // (IStartupFilter оборачивает конвейер целиком); запросы без заголовка —
        // без изменений. ADR-006: IP клиента = Connection.RemoteIpAddress.
        builder.ConfigureServices(services =>
            services.AddTransient<IStartupFilter, RemoteIpSubstitutionFilter>());

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(ToConfigKey(key), value);
        }

        // Служебный контроллер тестов: часть добавляется в ApplicationPartManager,
        // зарегистрированный AddControllers() в Program (коллбэк выполняется после
        // завершения Program); эндпойнты контроллеров вычисляются лениво — до первого
        // запроса, поэтому добавление части на старте хоста достаточно.
        builder.ConfigureServices(services =>
        {
            var partManager = services
                .FirstOrDefault(descriptor => descriptor.ServiceType == typeof(ApplicationPartManager))
                ?.ImplementationInstance as ApplicationPartManager
                ?? throw new InvalidOperationException(
                    "ApplicationPartManager не найден: AddControllers() должен вызываться в Program.");

            partManager.ApplicationParts.Add(new AssemblyPart(typeof(HostingSmokeController).Assembly));
        });
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");

    /// <summary>
    /// Тестовая подмена RemoteIpAddress соединения (IP-шов, см. шапку класса):
    /// эмулирует запросы с разных IP без сетевого стека.
    /// </summary>
    private sealed class RemoteIpSubstitutionFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, pipeline) =>
                {
                    var rawIp = context.Request.Headers[TestWebAppFactory.RemoteIpHeader].ToString();
                    if (IPAddress.TryParse(rawIp, out var remoteIp))
                    {
                        context.Connection.RemoteIpAddress = remoteIp;
                    }

                    await pipeline();
                });

                next(app);
            };
    }
}
