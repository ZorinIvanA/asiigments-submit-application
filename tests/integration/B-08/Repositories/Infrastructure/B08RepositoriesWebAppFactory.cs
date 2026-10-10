using System.Reflection;
using LabsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LabsApp.IntegrationTests.B08.Repositories.Infrastructure;

/// <summary>
/// Тестовый хост волны B-08 «репозитории» (кейсы TS-175..TS-178, зона
/// tests/integration/B-08/Repositories). Окружение — Development; секреты
/// Auth__JwtKey/Seed__TeacherPassword зафиксированы (ADR-010/FR-006),
/// Auth__Pbkdf2Iterations=1000 — тестовое умолчание спеки (FR-027: малые
/// итерации в тестовом хосте — детерминизм и скорость сид/KDF);
/// Seed__DemoData=false ЯВНО — кейсы волны не зависят от демо-набора
/// (AR-011/NFR-011): хранилище пусто, кроме сид-преподавателя, поэтому
/// занятость пар/имён/логинов given выполняется ровно (пара (5,1) свободна,
/// логин 'raceuser' свободен).
///
/// Сессии — POST /api/v1/auth/login по сид-учёткам (черноящичный
/// HTTP-контракт; успешный вход лимитером не ограничивается — LoginFailure-
/// Limiter считает только отказы); DI-сид студентов — по ADR-016 (обход
/// регистрационного лимита 5/час на IP в нагрузочном сценарии TS-178).
///
/// BL-001 (среда прогона): DOTNET_USE_POLLING_FILE_WATCHER=true — хосты
/// параллельных батчей не истощают inotify-лимит машины.
///
/// Изоляция сценариев: каждый тестовый КЛАСС со своей IClassFixture-фикстурой
/// получает свежий экземпляр приложения — чистые хранилища и лимитеры.
/// </summary>
public class B08RepositoriesWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Ключ подписи JWT тестового хоста (FR-006: ≥32 символа).</summary>
    public const string TestJwtKey = "b08-repos-integration-test-jwt-signing-key-0123456789abcdef";

    /// <summary>Пароль сид-преподавателя фикстуры (§8 удовлетворяет и в Production-правилах).</summary>
    public const string TestTeacherPassword = "b08-repos-teacher-password1!";

    /// <summary>Логин сид-преподавателя (умолчание Seed__TeacherLogin).</summary>
    public const string TeacherLogin = "teacher";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");

        // Имя окружения строкой: константа Environments в compile-поверхности
        // тестового проекта недоступна (aspnetcore 10 консолидировал
        // Hosting.Abstractions) — как в поддереве Groups/ этой же зоны.
        builder.UseEnvironment("Development");
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Ключи конфигурации — переменные окружения спеки с '__' → ':' для
        // UseSetting (при минимальном хостинге значения попадают в аргументы
        // точки входа до запуска Program; иерархию создаёт ':').
        builder.UseSetting("Auth:JwtKey", TestJwtKey);
        builder.UseSetting("Auth:Pbkdf2Iterations", "1000");
        builder.UseSetting("Seed:TeacherLogin", TeacherLogin);
        builder.UseSetting("Seed:TeacherPassword", TestTeacherPassword);
        builder.UseSetting("Seed:DemoData", "false");
    }
}

/// <summary>
/// Фикстура кейса TS-175 (FR-024 AC «Подмена реализации»): каждая штатная
/// DI-регистрация интерфейсов персистенции FR-024 — IUserRepository,
/// IGroupRepository, ILabRepository, ISubmissionRepository,
/// ISecurityTokenRepository, IRateLimitStore (имена зафиксированы FR-024) —
/// заменяется тестовой реализацией-декоратором <see cref="B08SubstitutionProxy"/>
/// (запись вызовов + переадресация штатному in-memory объекту), а штатные
/// дескрипторы удаляются. Контроллеры и сервисы при этом НЕ правятся вовсе:
/// запуск сквозного потока register → login → create group/lab → назначение
/// студента → upsert сдачи через подмененные реализации доказывает зависимость
/// только от интерфейсов. Поскольку состав членов интерфейсов в волне реворка
/// меняется (IF-015), декоратор строится поверх ЖИВОГО дескриптора DI и типа
/// интерфейса, найденного по зафиксированному FR-024 имени, — подмена не
/// компилируется против конкретных сигнатур и переживает реворк.
/// </summary>
public sealed class B08RepositoriesSubstitutionFactory : B08RepositoriesWebAppFactory
{
    /// <summary>Журнал подмен и вызовов тестовых реализаций (инспекция тестом).</summary>
    public B08SubstitutionRecorder Recorder { get; } = new();

    /// <summary>Имена интерфейсов персистенции, зафиксированные FR-024.</summary>
    public static readonly string[] PersistedInterfaceNames =
    [
        "IUserRepository",
        "IGroupRepository",
        "ILabRepository",
        "ISubmissionRepository",
        "ISecurityTokenRepository",
        "IRateLimitStore",
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(SubstitutePersistenceInterfaces);
    }

    private void SubstitutePersistenceInterfaces(IServiceCollection services)
    {
        var assemblyInterfaces = SafeInterfaceTypes(typeof(Program).Assembly);

        foreach (var interfaceName in PersistedInterfaceNames)
        {
            var interfaceType = assemblyInterfaces.FirstOrDefault(type => type.Name == interfaceName);
            if (interfaceType is null)
            {
                // Интерфейса с именем FR-024 нет в сборке — тест TS-175 сообщит
                // об отсутствии подмены (нарушение FR-024), хост не трогаем.
                continue;
            }

            var originalDescriptors = services
                .Where(descriptor => descriptor.ServiceType == interfaceType)
                .ToList();
            if (originalDescriptors.Count == 0)
            {
                continue;
            }

            foreach (var original in originalDescriptors)
            {
                services.Remove(original);
                services.Add(new ServiceDescriptor(
                    interfaceType,
                    serviceProvider => B08SubstitutionProxy.Create(
                        ResolveOriginal(serviceProvider, original),
                        interfaceType,
                        Recorder),
                    original.Lifetime));
            }

            Recorder.MarkSubstituted(interfaceName);
        }
    }

    private static object ResolveOriginal(IServiceProvider serviceProvider, ServiceDescriptor descriptor) =>
        descriptor.ImplementationInstance
        ?? descriptor.ImplementationFactory?.Invoke(serviceProvider)
        ?? (descriptor.ImplementationType is null
            ? throw new InvalidOperationException(
                $"Штатная реализация {descriptor.ServiceType.Name} не выражена дескриптором DI (FR-024).")
            : ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType));

    private static IReadOnlyList<Type> SafeInterfaceTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes().Where(type => type.IsInterface).ToList();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types
                .Where(type => type is { IsInterface: true })
                .Cast<Type>()
                .ToList();
        }
    }
}
