using System.Globalization;
using System.Text;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Infrastructure;
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
    public static async Task<Group> EnsureGroupAsync(B12WebAppFactory factory, string name)
    {
        var groups = factory.Services.GetRequiredService<IGroupRepository>();
        var existing = await groups.GetByNameAsync(name);
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
        await groups.AddAsync(group);
        return group;
    }

    /// <summary>
    /// Добавляет студента напрямую в IUserRepository (шаг given кейса; сессия будет
    /// минтиться, ADR-022 — пароль не используется). Повторный вызов с тем же login
    /// возвращает существующую запись (идемпотентность внутри класса-фикстуры).
    /// </summary>
    public static async Task<User> EnsureStudentAsync(
        B12WebAppFactory factory,
        string login,
        string fullName,
        string email,
        Guid? groupId = null)
    {
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var existing = await users.GetByLoginAsync(login);
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
        await users.AddAsync(user);
        return user;
    }

    /// <summary>uuid сид-преподавателя — шаг given «uuid преподавателя» (TS-145).</summary>
    public static async Task<Guid> TeacherIdAsync(B12WebAppFactory factory) =>
        (await factory.Services.GetRequiredService<IUserRepository>().GetByLoginAsync(TeacherLogin))?.Id
        ?? throw new InvalidOperationException(
            "Сид-преподаватель не найден — шаг given неисполним.");

    /// <summary>Собирает JSON-тело PUT /students/{id}/group из сырого JSON-значения groupId.</summary>
    public static StringContent GroupBody(string rawGroupIdJson) =>
        new(
            string.Create(CultureInfo.InvariantCulture, $"{{\"groupId\":{rawGroupIdJson}}}"),
            Encoding.UTF8,
            "application/json");
}
