using LabsApp.Infrastructure.Ef;
using LabsApp.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LabsApp.Infrastructure;

/// <summary>
/// Регистрации PostgreSQL-хранилища (итерация «БД»): один NpgsqlDataSource
/// (общий пул подключений и для EF Core, и для сырого Npgsql-репозитория сдач),
/// DbContext без миграций (схема создаётся скриптами src/api/db/02_schema.sql)
/// и scoped-репозитории. SeedRunner — scoped: разрешается из скоупа
/// SeedDatabaseAsync один раз на старт.
/// </summary>
public static class PostgresStorageServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresStorage(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        // Единый источник подключений на приложение: и EF Core, и сырой
        // Npgsql-репозиторий сдач работают через один пул.
        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        services.AddSingleton(dataSource);

        services.AddDbContext<LabsAppDbContext>(options => options.UseNpgsql(dataSource));

        // Репозитории по агрегатам: EF Core, кроме сдач — сырой Npgsql
        // (задача итерации «БД»: минимум один репозиторий на чистом ADO.NET).
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IGroupRepository, EfGroupRepository>();
        services.AddScoped<ILabRepository, EfLabRepository>();
        services.AddScoped<ISubmissionRepository, NpgsqlSubmissionRepository>();

        services.AddScoped<SeedRunner>();

        return services;
    }
}
