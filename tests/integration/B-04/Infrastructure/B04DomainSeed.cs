using LabsApp.Domain.Entities;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B04.Infrastructure;

/// <summary>
/// DI-сид сущностей кейсов батча (ADR-010: прямое наполнение репозиториев из
/// factory.Services — обход лимитера регистраций и зависимости от демо-набора).
/// Студенты кейсов не аутентифицируются (сессии — минтом, ADR-022), поэтому
/// passwordHash — непустая заглушка; пароль по ней не проверяется никогда.
/// Метки времени фиксированы — детерминизм независимо от системных часов.
/// </summary>
public static class B04DomainSeed
{
    private static readonly DateTime SeedMoment = new(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Добавляет группу в IGroupRepository и возвращает её (метка SeedMoment).</summary>
    public static Group AddGroup(IGroupRepository groups, string name)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = SeedMoment,
        };
        groups.Add(group);
        return group;
    }

    /// <summary>
    /// Добавляет пользователя role=student в IUserRepository и возвращает его;
    /// необязательная groupId переводит студента в указанную группу.
    /// </summary>
    public static User AddStudent(IUserRepository users, string login, Guid? groupId = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = $"{login}@b04.test",
            PasswordHash = "b04-di-seed-not-a-real-hash",
            FullName = $"B-04 {login}",
            Role = UserRoles.Student,
            GroupId = groupId,
            CreatedAt = SeedMoment,
        };
        users.Add(user);
        return user;
    }

    /// <summary>Добавляет работу (семестр, номер) в ILabRepository и возвращает её.</summary>
    public static Lab AddLab(ILabRepository labs, int semester, int number)
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
        labs.Add(lab);
        return lab;
    }
}
