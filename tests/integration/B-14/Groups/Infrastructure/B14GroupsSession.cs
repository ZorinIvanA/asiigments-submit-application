using LabsApp.Hosting.Configuration;
using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B14.Groups.Infrastructure;

/// <summary>
/// Клиент доменных кейсов групп батча B-14 (given «анонимный клиент»
/// TS-102..TS-106, TS-160): аутентификации в приложении нет — все эндпойнты
/// публичны, клиент создаётся фабрикой без cookie/токенов. Перед выдачей
/// утверждается наличие сид-преподавателя (сидится всегда, независимо от
/// Seed__DemoData) — предусловие «текущий пользователь — teacher».
/// </summary>
public static class B14GroupsSession
{
    /// <summary>
    /// Анонимный клиент тестового хоста; сид-преподаватель на месте (утверждение
    /// шага given).
    /// </summary>
    public static async Task<HttpClient> CreateTeacherAsync(WebApplicationFactory<Program> factory)
    {
        _ = await factory.Services.GetRequiredService<IUserRepository>()
            .GetByLoginAsync(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException(
                "Сид-преподаватель не найден в DI-хранилище тестового хоста (шаг given «текущий пользователь — teacher» неисполним).");

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }
}
