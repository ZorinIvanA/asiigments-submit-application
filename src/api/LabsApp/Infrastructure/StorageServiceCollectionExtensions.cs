using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.Infrastructure;

/// <summary>
/// Сид при старте (FR-004). Регистрации хранилища (PostgreSQL) —
/// <see cref="PostgresStorageServiceCollectionExtensions.AddPostgresStorage"/>;
/// in-memory реализации перенесены в тестовую инфраструктуру
/// (tests/TestInfrastructure) и в приложении отсутствуют.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Выполняет сид асинхронно при построении приложения, до начала обслуживания
    /// запросов (FR-004). Вызывается в Program.cs сразу после builder.Build().
    /// SeedRunner разрешается из скоупа: реализации хранилищ могут быть scoped
    /// (EF Core/PostgreSQL), при этом одиночный вызов на старт остаётся.
    /// </summary>
    public static async Task SeedDatabaseAsync(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        using var scope = app.ApplicationServices.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SeedRunner>().RunAsync();
    }
}
