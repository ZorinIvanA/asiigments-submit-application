using LabsApp.Auth.RateLimiting;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Тестовый хост кейса TS-134 «Подмена реализации» (FR-024 AC): Development-хост,
/// в котором ВСЕ ШЕСТЬ интерфейсов персистенции FR-024/IF-015 (IUserRepository,
/// IGroupRepository, ILabRepository, ISubmissionRepository, ISecurityTokenRepository,
/// IRateLimitStore) заменены на фиктивные реализации <see cref="B06FakeRepositories"/>
/// — регистрация выполняется в ConfigureServices фабрики ПОСЛЕ регистраций Program,
/// поэтому при одиночном разрешении побеждает последняя регистрация (механика зоны
/// B-06/ADR-015). Конкретные InMemory-классы остаются зарегистрированными, но
/// контроллеры и сервисы их не разрешают: они зависят ТОЛЬКО от интерфейсов —
/// замена не требует правок кода контроллеров (проверяется сквозным прогоном
/// регистрация → вход → GET /labs на подменных хранилищах).
///
/// Экземпляры подмен доступны тесту свойствами <see cref="Users"/>,
/// <see cref="Groups"/>, <see cref="Labs"/>, <see cref="Submissions"/>,
/// <see cref="SecurityTokens"/>, <see cref="RateLimitStore"/> (регистрация
/// готовыми экземплярами — Assert.Same подтверждает сам факт подмены).
///
/// Конфигурация хоста — как <see cref="B06WebAppFactory"/>: фиксированные
/// Auth__JwtKey и Seed__TeacherPassword, Seed__DemoData=false ЯВНО — сид
/// преподавателя ложится в ФЕЙК IUserRepository и ни от чего другого не зависит.
/// </summary>
public sealed class B06RepoSwapWebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b06-repo-swap-test-jwt-signing-key-0123456789abcdef";

    public const string TestTeacherPassword = "b06-repo-swap-teacher-password1!";

    /// <summary>Подмена IUserRepository.</summary>
    public FakeUserRepository Users { get; } = new();

    /// <summary>Подмена IGroupRepository (каскад удаления сбрасывает GroupId через <see cref="Users"/>).</summary>
    public FakeGroupRepository Groups { get; }

    /// <summary>Подмена ISubmissionRepository.</summary>
    public FakeSubmissionRepository Submissions { get; } = new();

    /// <summary>Подмена ILabRepository (каскад удаления — через <see cref="Submissions"/>).</summary>
    public FakeLabRepository Labs { get; }

    /// <summary>Подмена ISecurityTokenRepository (регистрируется AddAuthCore — заменяется той же механикой).</summary>
    public FakeSecurityTokenRepository SecurityTokens { get; }

    /// <summary>Подмена IRateLimitStore (состояние лимитеров регистраций/входов).</summary>
    public FakeRateLimitStore RateLimitStore { get; } = new();

    public B06RepoSwapWebAppFactory()
    {
        Groups = new FakeGroupRepository(Users);
        Labs = new FakeLabRepository(Submissions);
        SecurityTokens = new FakeSecurityTokenRepository(TimeProvider.System);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // См. B06WebAppFactory: вотчеры перечитывания конфигурации тестовому хосту
        // не нужны (пер-пользовательский лимит inotify в среде прогона ненадёжен).
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IUserRepository>(Users));
            services.Replace(ServiceDescriptor.Singleton<IGroupRepository>(Groups));
            services.Replace(ServiceDescriptor.Singleton<ILabRepository>(Labs));
            services.Replace(ServiceDescriptor.Singleton<ISubmissionRepository>(Submissions));
            services.Replace(ServiceDescriptor.Singleton<ISecurityTokenRepository>(SecurityTokens));
            services.Replace(ServiceDescriptor.Singleton<IRateLimitStore>(RateLimitStore));
        });
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
