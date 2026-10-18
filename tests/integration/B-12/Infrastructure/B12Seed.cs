using System.Globalization;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Infrastructure;

/// <summary>
/// DI-сид и помощники батча B-12 (ADR-010): прямое наполнение репозиториев тестового
/// хоста для шагов given (студенты с точными полями кейса, группы). Используются
/// только публичные интерфейсы IF-015 — код реализации не затрагивается. Обращение
/// к factory.Services материализует хост (сид выполняется при построении приложения
/// до первого запроса). Хелперы идемпотентны по уникальному ключу (login / имя
/// группы): несколько [Fact] одного класса пользуются одной фабрикой-фикстурой.
/// </summary>
public static class B12Seed
{
    /// <summary>Логин сид-преподавателя (SeedOptions) — для шагов given «uuid преподавателя».</summary>
    public const string TeacherLogin = SeedOptions.DefaultTeacherLogin;

    /// <summary>Добавляет группу напрямую в IGroupRepository (шаг given «группа существует»).</summary>
    public static Group EnsureGroup(B12WebAppFactory factory, string name)
    {
        var groups = factory.Services.GetRequiredService<IGroupRepository>();
        var existing = groups.GetByName(name);
        if (existing is not null)
        {
            return existing;
        }

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTime.UtcNow,
        };
        groups.Add(group);
        return group;
    }

    /// <summary>
    /// Добавляет студента напрямую в IUserRepository (шаг given кейса; сессия будет
    /// минтиться, ADR-022 — пароль не используется). Повторный вызов с тем же login
    /// возвращает существующую запись (идемпотентность внутри класса-фикстуры).
    /// </summary>
    public static User EnsureStudent(
        B12WebAppFactory factory,
        string login,
        string fullName,
        string email,
        Guid? groupId = null)
    {
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var existing = users.GetByLogin(login);
        if (existing is not null)
        {
            return existing;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            // Сессии минтятся через ITokenService (ADR-022) — пароль не используется.
            PasswordHash = "di-seed-mint-only",
            FullName = fullName,
            Role = UserRoles.Student,
            GroupId = groupId,
            CreatedAt = DateTime.UtcNow,
        };
        users.Add(user);
        return user;
    }

    /// <summary>uuid сид-преподавателя — шаг given «uuid преподавателя» (TS-145).</summary>
    public static Guid TeacherId(B12WebAppFactory factory) =>
        factory.Services.GetRequiredService<IUserRepository>().GetByLogin(TeacherLogin)?.Id
        ?? throw new InvalidOperationException(
            "Сид-преподаватель не найден — шаг given неисполним.");

    /// <summary>Собирает JSON-тело PUT /students/{id}/group из сырого JSON-значения groupId.</summary>
    public static StringContent GroupBody(string rawGroupIdJson) =>
        new(
            string.Create(CultureInfo.InvariantCulture, $"{{\"groupId\":{rawGroupIdJson}}}"),
            Encoding.UTF8,
            "application/json");
}
