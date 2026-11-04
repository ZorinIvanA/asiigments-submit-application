using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B14.Infrastructure;

/// <summary>
/// Предусловия тестового хоста батча B-14. Аутентификации в приложении нет:
/// клиенты анонимны, «текущий пользователь» профиля — засеянный преподаватель
/// (Seed__TeacherLogin; сидится всегда и идемпотентно, независимо от
/// Seed__DemoData). Данные шагов given создаются прямым DI-сидом в репозиториях
/// тестового хоста (методика NFR-001/FR-007/ADR-010); чтения возвращают
/// копии-снимки (контракт IF-015).
/// </summary>
public static class B14Harness
{
    public const string ProfileEndpoint = "/api/v1/me/profile";

    /// <summary>
    /// Клиент без авто-редиректов (точные статусы). Аутентификации нет —
    /// cookie-механика сессий не нужна.
    /// </summary>
    public static HttpClient Create(B14WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// given «текущий пользователь — сид-преподаватель» (TS-196, TS-173):
    /// сид-учётка FR-004 (login — умолчание SeedOptions.DefaultTeacherLogin,
    /// GroupId=null); утверждение наличия и роли — предусловие кейса.
    /// </summary>
    public static async Task<User> ResolveSeedTeacherAsync(B14WebAppFactory factory)
    {
        var teacher = await factory.Services.GetRequiredService<IUserRepository>()
            .GetByLoginAsync(SeedOptions.DefaultTeacherLogin);
        Assert.True(
            teacher is not null,
            "Предусловие кейса: сид-преподаватель отсутствует в хранилище тестового хоста.");
        Assert.Equal(UserRoles.Teacher, teacher!.Role);
        Assert.Null(teacher.GroupId);
        return teacher;
    }
}
