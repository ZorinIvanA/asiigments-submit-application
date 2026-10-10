using System.Globalization;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>
/// DI-сид и помощники батча B-13 (ADR-010): прямое наполнение репозиториев
/// тестового хоста для шагов given (группы, студенты с известным порядком
/// сортировки, лабы заданных семестров, сдачи заданных пар). Используются только
/// публичные интерфейсы IF-015 — код реализации не затрагивается. Обращение к
/// factory.Services материализует хост (сид преподавателя выполняется при
/// построении приложения до первого запроса; демо-набор выключен — AR-011).
/// </summary>
public static class B13Seed
{
    /// <summary>Добавляет группу напрямую в IGroupRepository (DI-сид кейса, ADR-010).</summary>
    public static Group AddGroup(B13WebAppFactory factory, string name)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTime.UtcNow,
        };
        factory.Services.GetRequiredService<IGroupRepository>().Add(group);
        return group;
    }

    /// <summary>
    /// Добавляет студента напрямую в IUserRepository (сессия будет минтиться, ADR-022);
    /// groupId — членство в группе шага given (null = без группы).
    /// </summary>
    public static User AddStudent(B13WebAppFactory factory, string login, string fullName, Guid? groupId)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = string.Create(CultureInfo.InvariantCulture, $"{login}@example.com"),
            // Сессии минтятся через ITokenService (ADR-022) — пароль не используется.
            PasswordHash = "di-seed-mint-only",
            FullName = fullName,
            Role = UserRoles.Student,
            GroupId = groupId,
            CreatedAt = DateTime.UtcNow,
        };
        factory.Services.GetRequiredService<IUserRepository>().Add(user);
        return user;
    }

    /// <summary>Добавляет работу напрямую в ILabRepository (DI-сид кейса, ADR-010).</summary>
    public static Lab AddLab(B13WebAppFactory factory, int semester, int number, bool defenseRequired = false)
    {
        var lab = new Lab
        {
            Id = Guid.NewGuid(),
            Semester = semester,
            Number = number,
            Content = string.Create(CultureInfo.InvariantCulture, $"Содержание (DI-сид) {semester}:{number}"),
            AssignmentUrl = null,
            DefenseRequired = defenseRequired,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        factory.Services.GetRequiredService<ILabRepository>().Add(lab);
        return lab;
    }

    /// <summary>
    /// Добавляет сдачу пары (studentId, labId) напрямую в ISubmissionRepository —
    /// атомарный upsert IF-015; updatedBy — uuid сид-преподавателя (как у записи,
    /// изменённой преподавателем). Даты — строки 'YYYY-MM-DD' (null = даты нет).
    /// </summary>
    public static void AddSubmission(
        B13WebAppFactory factory,
        Guid studentId,
        Guid labId,
        string? submitDate,
        string? defenseDate)
    {
        var teacherId = factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)?.Id
            ?? throw new InvalidOperationException(
                "Сид-преподаватель не найден — сдача не может быть создана.");
        factory.Services.GetRequiredService<ISubmissionRepository>().Upsert(
            studentId,
            labId,
            ParseDate(submitDate),
            ParseDate(defenseDate),
            teacherId,
            DateTime.UtcNow);
    }

    /// <summary>
    /// Семь DI-сид-студентов группы с известным порядком сортировки
    /// (fullName ci (ru), затем login ci — FR-060): Алексеев, Борисов, Васильев,
    /// Григорьев, Дмитриев, Егоров, Жуков. Возвращает студентов В ЭТОМ порядке
    /// (1-й … 7-й); при pageSize=5 страница 2 — 6-й и 7-й (Егоров, Жуков).
    /// </summary>
    public static IReadOnlyList<User> AddSevenKnownOrderStudents(B13WebAppFactory factory, Guid groupId)
    {
        var distinctNames = new[]
        {
            "Алексеев", "Борисов", "Васильев", "Григорьев", "Дмитриев", "Егоров", "Жуков",
        };
        var students = new List<User>(distinctNames.Length);
        for (var i = 0; i < distinctNames.Length; i++)
        {
            students.Add(AddStudent(
                factory,
                $"ts148-s{(i + 1).ToString("00", CultureInfo.InvariantCulture)}",
                distinctNames[i],
                groupId));
        }

        return students;
    }

    private static DateOnly? ParseDate(string? value) =>
        value is null
            ? null
            : DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
