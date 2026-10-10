using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B05.Infrastructure;

/// <summary>
/// Тестовый хост сценариев безопасности батча B-05 (TS-033..TS-037, TS-163..TS-170;
/// FR-010/FR-012/FR-080, NFR-004/NFR-005). Собственная копия механики фабрики
/// (фабрики прежнего состава зоны и src/api/LabsApp.Tests недоступны для
/// наследования/ссылок — изоляция зон, BL-001 BUG-001; образец — зоны B-07/B-08):
///  - Environment=Development и Seed__DemoData=false ЯВНО — кейсы батча не зависят
///    от демо-набора (AR-011/NFR-011), пользователи создаются DI-сидом или
///    POST /auth/register; Seed__TeacherPassword='Passw0rd!' — given кейсов со
///    входом преподавателя (TS-037, TS-164; в Development требования §8 к
///    сид-паролю не применяются, FR-006; значение удовлетворяет §8);
///  - бизнес-время — <see cref="B05FakeTimeProvider"/> (TimeProvider — единый
///    источник, глоссарий): окно лимитера входа детерминировано (TS-164);
///  - IClientIpResolver декорирован <see cref="B05TestClientIpResolver"/> —
///    транспортный IP клиента задаётся заголовком (given «RemoteIpAddress=
///    10.0.0.x», TS-163/164/165; без заголовка — боевой резолвер);
///  - log-sink <see cref="B05LogSink"/> — извлечение кода из Email.Dev (TS-035)
///    и проверка отсутствия Email.Dev-событий (TS-166).
///
/// Каждый IClassFixture-класс — свежий экземпляр приложения: пустые лимитеры,
/// пустое хранилище (кроме сид-преподавателя) и чистый sink — изоляция given.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
/// </summary>
public class B05SecurityWebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b05-security-test-jwt-signing-key-0123456789abcdef";
    public const string TestTeacherPassword = "Passw0rd!";

    /// <summary>Бизнес-время теста: сдвигается только вызовами из сценария.</summary>
    public B05FakeTimeProvider Time { get; } = new();

    /// <summary>Log-sink тестового хоста; изоляция — Clear() перед сценарием.</summary>
    public B05LogSink LogSink { get; } = new();

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B05SecurityWebAppFactory()
        : this(Environments.Development, new Dictionary<string, string?>())
    {
    }

    /// <summary>Внутренний конструктор для производных фикстур (окружение/настройки).</summary>
    protected B05SecurityWebAppFactory(string environment, IReadOnlyDictionary<string, string?> settings)
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

        // Кейсы батча опираются только на собственных пользователей (сиды DI и
        // регистрации эндпоинтом): демо-набор отключён ЯВНО (AR-011/NFR-011).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        // Log-sink в конвейере журналирования тестового хоста (TS-035, TS-166).
        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(ToConfigKey(key), value);
        }

        builder.ConfigureServices(services =>
        {
            // Единый источник бизнес-времени теста: подмена последней регистрацией —
            // все потребители (JWT, лимитеры, хранилища) получают Time (глоссарий).
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);

            // Транспортный IP клиента — по заголовку TestIpHeader (контракт IF-006).
            services.Replace(ServiceDescriptor.Singleton<IClientIpResolver>(
                _ => new B05TestClientIpResolver(new ClientIpResolver())));
        });
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}

/// <summary>
/// Фикстура кейса TS-169 (доверенный прокси): ForwardedHeaders__KnownProxies=
/// 127.0.0.1 — UseForwardedHeadersIfConfigured подключает middleware, и
/// X-Forwarded-For соединений с 127.0.0.1 определяет итоговый адрес клиента
/// (ключи лимитеров). Прочие кейсы идут с пустым списком (TS-168).
/// </summary>
public sealed class B05ProxyWebAppFactory : B05SecurityWebAppFactory
{
    public B05ProxyWebAppFactory()
        : base(
            Environments.Development,
            new Dictionary<string, string?>
            {
                [ForwardedProxyOptions.KnownProxiesVariable] = "127.0.0.1",
            })
    {
    }
}

/// <summary>
/// Фикстура кейсов потолка ключей (TS-166, TS-167) с ЯВНЫМ
/// RateLimits__MaxTrackedKeys=10000 — значение из given кейса и умолчания FR-006:
/// потолок ключей RecoveryRequestLimiter фиксирован независимо от конфигурации.
/// </summary>
public sealed class B05CeilingWebAppFactory : B05SecurityWebAppFactory
{
    public B05CeilingWebAppFactory()
        : base(
            Environments.Development,
            new Dictionary<string, string?>
            {
                [RateLimitsOptions.MaxTrackedKeysVariable] = "10000",
            })
    {
    }
}

/// <summary>
/// Фикстура кейса TS-037 с ЯВНЫМ Auth__AccessTtlMinutes=15 — значение из given
/// кейса: exp−iat access-JWT обязан быть ровно 15*60 секунд.
/// </summary>
public sealed class B05AccessTtlWebAppFactory : B05SecurityWebAppFactory
{
    public B05AccessTtlWebAppFactory()
        : base(
            Environments.Development,
            new Dictionary<string, string?>
            {
                [AuthOptions.AccessTtlMinutesVariable] = "15",
            })
    {
    }
}
