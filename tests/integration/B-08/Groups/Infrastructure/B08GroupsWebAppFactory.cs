using LabsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace LabsApp.IntegrationTests.B08.Groups.Infrastructure;

/// <summary>
/// Тестовый хост сценариев групп/репозиториев батча B-08 (зона
/// tests/integration/B-08/Groups). Окружение — Development; секреты
/// Auth__JwtKey/Seed__TeacherPassword зафиксированы (ADR-010/FR-006),
/// Auth__Pbkdf2Iterations=1000 — тестовое умолчание спеки (FR-027:
/// малые итерации в тестовом хосте, детерминизм и скорость сид/KDF);
/// Seed__DemoData задаётся ЯВНО производными фикстурами.
///
/// Данные сценариев — демо-сид спеки (Seed__DemoData=true точно
/// воспроизводит src/client/app/mock/seed.ts: группы ИК-221 (25 студентов),
/// ИК-222 (5), ИК-223 (0), студенты student01..student32) либо пустое
/// хранилище с сид-преподавателем; сессии — POST /api/v1/auth/login по
/// сид-учёткам (черноящичный HTTP-контракт, без привязки к internals auth).
///
/// Изоляция сценариев: каждый тестовый КЛАСС со своей IClassFixture-фикстурой
/// получает свежий экземпляр приложения — чистые хранилища, лимитеры и сид,
/// поэтому занятость имён/пар в given выполняется ровно.
///
/// BL-001 (среда прогона): DOTNET_USE_POLLING_FILE_WATCHER=true — хосты
/// параллельных батчей не истощают inotify-лимит машины.
/// </summary>
public class B08GroupsWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Ключ подписи JWT тестового хоста (FR-006: ≥32 символа).</summary>
    public const string TestJwtKey = "b08-groups-integration-test-jwt-signing-key-0123456789abcdef";

    /// <summary>Пароль сид-преподавателя фикстуры (§8 удовлетворяет и в Production-правилах).</summary>
    public const string TestTeacherPassword = "b08-groups-teacher-password1!";

    /// <summary>Логин сид-преподавателя (умолчание Seed__TeacherLogin).</summary>
    public const string TeacherLogin = "teacher";

    /// <summary>Демо-пароль сид-студентов mock/seed.ts (STUDENT_PASSWORD).</summary>
    public const string SeedStudentPassword = "student123!";

    /// <summary>Демо-набор включён (true: ИК-221/222/223 + student01..32) или пустое хранилище.</summary>
    protected virtual bool SeedDemoData => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");

        // Имя окружения строкой: константа Environments в compile-поверхности
        // тестового проекта недоступна (aspnetcore 10 консолидировал Hosting.Abstractions).
        builder.UseEnvironment("Development");
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Ключи конфигурации — переменные окружения спеки с '__' → ':' для
        // UseSetting (при минимальном хостинге значения попадают в аргументы
        // точки входа до запуска Program; иерархию создаёт ':').
        builder.UseSetting("Auth:JwtKey", TestJwtKey);
        builder.UseSetting("Auth:Pbkdf2Iterations", "1000");
        builder.UseSetting("Seed:TeacherLogin", TeacherLogin);
        builder.UseSetting("Seed:TeacherPassword", TestTeacherPassword);
        builder.UseSetting("Seed:DemoData", SeedDemoData ? "true" : "false");
    }
}

/// <summary>
/// Фикстура с демо-сидом «ИК-221 (25 студентов), ИК-222 (5), ИК-223 (0)»
/// (данные given снесённых кейсов TS-122/TS-124/TS-125/TS-127/TS-204 —
/// владелец кейсов зона tests/integration/B-17/Groups; оставшимися кейсами
/// зоны TS-165..TS-169 не используется).
/// </summary>
public sealed class B08GroupsDemoDataFactory : B08GroupsWebAppFactory
{
    protected override bool SeedDemoData => true;
}

/// <summary>
/// Фикстура кейса TS-165 (FR-024 AC «Подмена реализации»): каждая штатная
/// DI-регистрация интерфейсов персистенции FR-024 — IUserRepository,
/// IGroupRepository, ILabRepository, ISubmissionRepository,
/// ISecurityTokenRepository, IRateLimitStore (имена зафиксированы FR-024) —
/// заменяется тестовой реализацией-декоратором <see cref="B08SubstitutionProxy"/>
/// (запись вызовов + переадресация штатному in-memory объекту), а штатные
/// дескрипторы удаляются. Контроллеры и сервисы при этом НЕ правятся вовсе:
/// запуск базового сценария register → login → GET /labs через подмененные
/// реализации доказывает зависимость только от интерфейсов. Посколько состав
/// членов интерфейсов в волне реворка меняется (IF-015), декоратор строится
/// поверх ЖИВОГО дескриптора DI и типа интерфейса, найденного по
/// зафиксированному FR-024 имени, — подмена не компилируется против
/// конкретных сигнатур и переживает реворк.
/// </summary>
public sealed class B08GroupsSubstitutionFactory : B08GroupsWebAppFactory
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
                // Интерфейса с именем FR-024 нет в сборке — тест TS-165 сообщит
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
