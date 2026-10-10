using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B22.Infrastructure;

/// <summary>
/// DI-сид доменных данных батча B-22 (given TS-184/NFR-001): прямое наполнение
/// in-memory репозиториев из factory.Services, минуя публичный API (объёмные
/// данные готовятся в обход HTTP-лимитов; регистрационный лимитер 5/час на IP —
/// FR-004 — прервал бы засев на 6-м запросе, поэтому создание через
/// POST /auth/register недопустимо). Идемпотентен: существующие записи
/// пропускаются. Пароли DI-пользователей кейсами не используются — PasswordHash
/// кладётся заглушкой, PBKDF2 при сиде не вызывается (KDF-зависимые
/// эндпойнты кейсом TS-184 исключены).
/// </summary>
public static class B22DomainSeed
{
    /// <summary>
    /// Работа заданной пары (semester, number): добавляется в ILabRepository, если пара
    /// свободна; иначе возвращается существующая запись.
    /// </summary>
    public static Lab AddLab(WebApplicationFactory<Program> factory, int semester, int number)
    {
        var labs = factory.Services.GetRequiredService<ILabRepository>();
        if (labs.TryGetByPair(semester, number) is { } existing)
        {
            return existing;
        }

        var lab = new Lab
        {
            Id = Guid.NewGuid(),
            Semester = semester,
            Number = number,
            Content = $"Содержание DI-сида работы B-22 {semester}:{number}",
            AssignmentUrl = null,
            DefenseRequired = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        labs.Add(lab);
        return lab;
    }

    /// <summary>Группа с заданным именем (если имя свободно — добавляется, иначе существующая).</summary>
    public static Group AddGroup(WebApplicationFactory<Program> factory, string name)
    {
        var groups = factory.Services.GetRequiredService<IGroupRepository>();
        if (groups.GetByName(name) is { } existing)
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
    /// Учётная запись студента role=student с заданной группой (GroupId = null — без группы).
    /// </summary>
    public static User AddStudent(WebApplicationFactory<Program> factory, string login, Guid? groupId = null)
    {
        var users = factory.Services.GetRequiredService<IUserRepository>();
        if (users.GetByLogin(login) is { } existing)
        {
            return existing;
        }

        var student = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = $"{login}@example.com",
            FullName = $"Тестовый Студент B-22 {login}",
            Role = UserRoles.Student,
            GroupId = groupId,
            PasswordHash = "b22-di-seed-hash-not-used-by-cases",
            CreatedAt = DateTime.UtcNow,
        };
        users.Add(student);
        return student;
    }

    /// <summary>Итог сида производительности NFR-001 (кейс TS-184).</summary>
    public sealed record PerformanceDataset(Guid TargetGroupId, int TargetSemester, int TargetSemesterLabCount);

    /// <summary>
    /// Датасет производительности NFR-001 (given TS-184): 200 работ, 300
    /// пользователей, группа 25 студентов × семестр 20 работ. Целевой семестр — 7
    /// (работы с номерами 1..20); остальные 180 работ — шесть прочих семестров
    /// {1..6} по 30 работ (итого ровно 200). Целевая группа «B184-Perf-Group»
    /// вмещает первых 25 студентов; остальные 275 — вне группы.
    /// KDF-зависимые поверхности не задействованы.
    /// </summary>
    public static PerformanceDataset SeedPerformanceDataset(B22WebAppFactory factory)
    {
        const int targetSemester = 7;
        const int targetSemesterLabs = 20;
        const int otherSemesterLabs = 30;
        const int totalStudents = 300;
        const int targetGroupSize = 25;

        // 200 работ: целевой семестр (20) + шесть прочих семестров (6 × 30 = 180).
        for (var number = 1; number <= targetSemesterLabs; number++)
        {
            AddLab(factory, targetSemester, number);
        }

        foreach (var semester in new[] { 1, 2, 3, 4, 5, 6 })
        {
            for (var number = 1; number <= otherSemesterLabs; number++)
            {
                AddLab(factory, semester, number);
            }
        }

        // 300 пользователей, первые 25 — в целевой группе.
        var targetGroup = AddGroup(factory, "B184-Perf-Group");
        for (var i = 1; i <= totalStudents; i++)
        {
            var inTargetGroup = i <= targetGroupSize;
            AddStudent(
                factory,
                $"b184-perf-student-{i:D4}",
                inTargetGroup ? targetGroup.Id : null);
        }

        return new PerformanceDataset(targetGroup.Id, targetSemester, targetSemesterLabs);
    }
}
