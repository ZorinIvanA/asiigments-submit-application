using LabsApp.Domain.Entities;
using LabsApp.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Infrastructure;

/// <summary>
/// DI-сид сущностей кейсов батча (ADR-010: прямое наполнение репозиториев из
/// factory.Services — независимость от демо-набора). Студенты кейсов —
/// участники доменных данных (сдач, групп); пароль по passwordHash никогда
/// не проверяется (аутентификации в приложении нет), хэш — плейсхолдер сида.
/// Метки времени фиксированы — детерминизм независимо от системных часов.
/// </summary>
public static class B04DomainSeed
{
    private static readonly DateTime SeedMoment = new(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Добавляет группу в IGroupRepository и возвращает её (метка SeedMoment).</summary>
    public static async Task<Group> AddGroupAsync(IGroupRepository groups, string name)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = SeedMoment,
        };
        await groups.AddAsync(group);
        return group;
    }

    /// <summary>
    /// Добавляет пользователя role=student в IUserRepository и возвращает его;
    /// необязательная groupId переводит студента в указанную группу.
    /// </summary>
    public static async Task<User> AddStudentAsync(IUserRepository users, string login, Guid? groupId = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = $"{login}@b04.test",
            PasswordHash = "!",
            FullName = $"B-04 {login}",
            Role = UserRoles.Student,
            GroupId = groupId,
            CreatedAt = SeedMoment,
        };
        await users.AddAsync(user);
        return user;
    }

    /// <summary>Добавляет работу (семестр, номер) в ILabRepository и возвращает её.</summary>
    public static async Task<Lab> AddLabAsync(ILabRepository labs, int semester, int number)
    {
        var lab = new Lab
        {
            Id = Guid.NewGuid(),
            Semester = semester,
            Number = number,
            Content = $"B-04 интеграционный сид работы (семестр {semester}, номер {number})",
            AssignmentUrl = null,
            DefenseRequired = false,
            CreatedAt = SeedMoment,
            UpdatedAt = SeedMoment,
        };
        await labs.AddAsync(lab);
        return lab;
    }
}
