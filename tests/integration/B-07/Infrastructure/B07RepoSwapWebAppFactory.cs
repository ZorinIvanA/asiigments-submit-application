using LabsApp.Auth.RateLimiting;
using LabsApp.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LabsApp.IntegrationTests.B07.Infrastructure;

/// <summary>
/// Тестовый хост кейса TS-134 «Репозитории: подмена реализаций не требует правок
/// контроллеров» (FR-024 AC «Подмена реализации»): Development-хост зоны B-07
/// (<see cref="B07WebAppFactory"/> — окружение, конфигурация, log-sink), в котором
/// ВСЕ ШЕСТЬ интерфейсов персистенции FR-024/IF-015 (IUserRepository,
/// IGroupRepository, ILabRepository, ISubmissionRepository,
/// ISecurityTokenRepository, IRateLimitStore) заменены фиктивными реализациями
/// <see cref="B07FakeRepositories"/>. Регистрация выполняется в ConfigureServices
/// фабрики ПОСЛЕ регистраций Program (Replace), поэтому при одиночном разрешении
/// побеждает последняя регистрация. Конкретные InMemory-классы остаются
/// зарегистрированными, но контроллеры и сервисы их не разрешают: они зависят
/// ТОЛЬКО от интерфейсов — замена не требует правок кода контроллеров
/// (проверяется сквозным прогоном регистрация → вход → GET /labs на подменных
/// хранилищах). Сид-преподаватель (SeedRunner резолвит только интерфейсы)
/// ложится в ФЕЙК IUserRepository.
///
/// Экземпляры подмен доступны тесту свойствами <see cref="Users"/>,
/// <see cref="Groups"/>, <see cref="Labs"/>, <see cref="Submissions"/>,
/// <see cref="SecurityTokens"/>, <see cref="RateLimitStore"/> (регистрация
/// готовыми экземплярами — Assert.Same подтверждает сам факт подмены).
/// </summary>
public sealed class B07RepoSwapWebAppFactory : B07WebAppFactory
{
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

    public B07RepoSwapWebAppFactory()
    {
        Groups = new FakeGroupRepository(Users);
        Labs = new FakeLabRepository(Submissions);
        SecurityTokens = new FakeSecurityTokenRepository(TimeProvider.System);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

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
}
