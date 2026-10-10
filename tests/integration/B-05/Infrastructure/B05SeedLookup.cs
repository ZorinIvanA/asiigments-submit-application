using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
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
    public static User Teacher(B05WebAppFactory factory) =>
        factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException(
                "Сид-преподаватель не найден — given кейса неисполним.");

    /// <summary>Студент демо-сида по логину (studentNN).</summary>
    public static User StudentByLogin(B05WebAppFactory factory, string login) =>
        factory.Services.GetRequiredService<IUserRepository>().GetByLogin(login)
        ?? throw new InvalidOperationException(
            $"Студент {login} не найден в демо-сиде — given кейса неисполним.");

    /// <summary>Группа демо-сида по имени (ИК-221/ИК-222/ИК-223).</summary>
    public static Group GroupByName(B05WebAppFactory factory, string name) =>
        factory.Services.GetRequiredService<IGroupRepository>().GetByName(name)
        ?? throw new InvalidOperationException(
            $"Группа {name} не найдена в демо-сиде — given кейса неисполним.");
}
