using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Клиенты и данные сида кейсов волны сдач B-06. Аутентификации в приложении
/// нет: все эндпоинты анонимны, «текущий пользователь» — засеянный преподаватель
/// (Seed__TeacherLogin), поэтому клиент создаётся фабрикой без всяких cookie/токенов.
/// Сущности сида (преподаватель, студенты демо-набора, группа ИК-221, работы пар)
/// читаются из DI-репозиториев тестового хоста.
/// </summary>
public static class B06SubmissionsSessions
{
    /// <summary>
    /// Анонимный клиент хоста + uuid текущего пользователя — сид-преподавателя
    /// (TS-143/TS-147/TS-148/TS-155/TS-157/TS-158: атрибуция updatedBy).
    /// </summary>
    public static async Task<(HttpClient Client, Guid TeacherId)> CreateTeacherClientAsync(
        WebApplicationFactory<Program> factory)
    {
        var teacher = await RequireUserAsync(factory, SeedOptions.DefaultTeacherLogin);
        return (HostClients.Create(factory), teacher.Id);
    }

    /// <summary>Учётная запись демо-набора по логину либо падение шага given.</summary>
    public static async Task<User> RequireUserAsync(WebApplicationFactory<Program> factory, string login) =>
        await factory.Services.GetRequiredService<IUserRepository>().GetByLoginAsync(login)
        ?? throw new InvalidOperationException(
            $"Пользователь «{login}» не найден в DI-хранилище тестового хоста (демо-набор сида не развёрнут; шаг given неисполним).");

    /// <summary>Группа демо-набора по имени (ИК-221) либо падение шага given.</summary>
    public static async Task<Group> RequireGroupAsync(WebApplicationFactory<Program> factory, string name) =>
        await factory.Services.GetRequiredService<IGroupRepository>().GetByNameAsync(name)
        ?? throw new InvalidOperationException(
            $"Группа «{name}» не найдена в DI-хранилище тестового хоста (демо-набор сида не развёрнут; шаг given неисполним).");

    /// <summary>Работа демо-набора по паре (семестр, номер) либо падение шага given.</summary>
    public static async Task<Lab> RequireLabAsync(WebApplicationFactory<Program> factory, int semester, int number) =>
        await factory.Services.GetRequiredService<ILabRepository>().TryGetByPairAsync(semester, number)
        ?? throw new InvalidOperationException(
            $"Работа {semester}:{number} не найдена в DI-хранилище тестового хоста (демо-набор сида не развёрнут; шаг given неисполним).");
}
