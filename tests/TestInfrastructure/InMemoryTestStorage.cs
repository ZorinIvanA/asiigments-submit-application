using LabsApp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.TestInfrastructure;

/// <summary>
/// Регистрация in-memory двоек хранилища в тестовом хосте. Вызывается из
/// ConfigureWebHost тестовых фабрик ПОСЛЕ регистраций Program.cs: поздние
/// регистрации выигрывают, поэтому репозитории, SeedRunner и токенное
/// хранилище разрешаются в двойки, а подключение к PostgreSQL не выполняется
/// (NpgsqlDataSource и DbContext остаются зарегистрированными, но никем
/// не используются).
/// </summary>
public static class InMemoryTestStorage
{
    /// <summary>
    /// Подменяет регистрации хранилища in-memory двойками: интерфейсы
    /// репозиториев и SeedRunner. Пары «конкретный
    /// класс + интерфейс» нужны межрепозиторным каскадам (группы сбрасывают
    /// GroupId, работы удаляют сдачи) под общей <see cref="StorageLock"/>.
    /// </summary>
    public static IServiceCollection Register(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<StorageLock>();

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
}
