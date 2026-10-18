using LabsApp.Storage;
using LabsApp.Storage.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.Storage;

/// <summary>
/// Регистрации in-memory хранилища (C-003, IF-015, FR-002) и сида (FR-004).
/// Все реализации — DI-singleton с единой <see cref="StorageLock"/> (ADR-002);
/// контроллеры и сервисы зависят только от интерфейсов, конкретные классы
/// используются только в композиция-корне и тестах.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует in-memory репозитории IF-015 (по агрегатам, singleton) и
    /// <see cref="SeedRunner"/>. Вызывается один раз из Program.cs. Токенные
    /// хранилища (ISecurityTokenRepository — консолидация T-101) регистрирует
    /// AddAuthCore: единый писатель DI-регистраций Auth (ADR-030).
    /// </summary>
    public static IServiceCollection AddInMemoryStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<StorageLock>();

        // Пары «конкретный класс + интерфейс» нужны межрепозиторным каскадам:
        // группы сбрасывают GroupId через пользовательское хранилище, работы
        // удаляют сдачи через хранилище сдач — под общей StorageLock.
        services.AddSingleton<InMemoryUserRepository>();
        services.AddSingleton<IUserRepository>(static sp => sp.GetRequiredService<InMemoryUserRepository>());

        services.AddSingleton<InMemoryGroupRepository>();
        services.AddSingleton<IGroupRepository>(static sp => sp.GetRequiredService<InMemoryGroupRepository>());

        services.AddSingleton<InMemorySubmissionRepository>();
        services.AddSingleton<ISubmissionRepository>(static sp => sp.GetRequiredService<InMemorySubmissionRepository>());

        services.AddSingleton<InMemoryLabRepository>();
        services.AddSingleton<ILabRepository>(static sp => sp.GetRequiredService<InMemoryLabRepository>());

        services.AddSingleton<SeedRunner>();

        return services;
    }

    /// <summary>
    /// Выполняет сид синхронно при построении приложения, до начала обслуживания
    /// запросов (FR-004). Вызывается в Program.cs сразу после builder.Build().
    /// </summary>
    public static void SeedDatabase(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.ApplicationServices.GetRequiredService<SeedRunner>().Run();
    }
}
