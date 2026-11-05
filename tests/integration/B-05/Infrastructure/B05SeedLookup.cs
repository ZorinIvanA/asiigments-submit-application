using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Infrastructure;

/// <summary>
/// Считывание uuid сид-сущностей из репозиториев тестового хоста (ADR-015):
/// кейсы FR-020/FR-021 ссылаются на studentNN / ИК-221 / ИК-223 демо-сида FR-004,
/// не завися от эндпойнтов других волн (/auth/me, GET /groups). Каждый тестовый
/// КЛАСС получает свою IClassFixture-фабрику — сид свежий и детерминированный.
/// </summary>
public static class B05SeedLookup
{
    /// <summary>Сид-преподаватель (uuid, который вернул бы /auth/me, ADR-015).</summary>
    public static async Task<User> TeacherAsync(B05WebAppFactory factory) =>
        await factory.Services.GetRequiredService<IUserRepository>()
                .GetByLoginAsync(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException(
                "Сид-преподаватель не найден — given кейса неисполним.");

    /// <summary>Студент демо-сида по логину (studentNN).</summary>
    public static async Task<User> StudentByLoginAsync(B05WebAppFactory factory, string login) =>
        await factory.Services.GetRequiredService<IUserRepository>().GetByLoginAsync(login)
        ?? throw new InvalidOperationException(
            $"Студент {login} не найден в демо-сиде — given кейса неисполним.");

    /// <summary>Группа демо-сида по имени (ИК-221/ИК-222/ИК-223).</summary>
    public static async Task<Group> GroupByNameAsync(B05WebAppFactory factory, string name) =>
        await factory.Services.GetRequiredService<IGroupRepository>().GetByNameAsync(name)
        ?? throw new InvalidOperationException(
            $"Группа {name} не найдена в демо-сиде — given кейса неисполним.");
}
