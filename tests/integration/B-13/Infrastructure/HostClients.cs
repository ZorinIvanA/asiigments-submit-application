using LabsApp.Hosting.Configuration;
using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>
/// Клиент тестового хоста B-13: без авто-редиректов (точные статусы).
/// Аутентификации в приложении нет — клиент анонимный; «текущий пользователь»
/// всех /me/* эндпойнтов — засеянный преподаватель (Seed__TeacherLogin).
/// </summary>
public static class HostClients
{
    public static HttpClient Create(B13WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// Анонимный клиент хоста; перед выдачей проверяется, что сид-преподаватель
    /// (текущий пользователь) на месте — шаг given «анонимный клиент» исполним.
    /// </summary>
    public static async Task<HttpClient> CreateTeacherClientAsync(B13WebAppFactory factory)
    {
        _ = await factory.Services.GetRequiredService<IUserRepository>()
                .GetByLoginAsync(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException(
                "Сид-преподаватель не найден — шаг given «анонимный клиент (текущий пользователь — teacher)» неисполним.");
        return Create(factory);
    }
}
