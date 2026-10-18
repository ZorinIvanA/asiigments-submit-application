using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
// GlobalUsings зоны подключают System.Text.RegularExpressions — «Group» неоднозначен.
using Group = LabsApp.Domain.Entities.Group;

namespace LabsApp.IntegrationTests.B19.Infrastructure;

/// <summary>
/// DI-сид доменных данных батча B-19 (given TS-148/NFR-001): прямое наполнение
/// in-memory репозиториев из factory.Services, минуя публичный API (объёмные
/// данные готовятся в обход HTTP-лимитов; регистрационный лимитер 5/час на IP —
/// FR-004 — прервал бы засев на 6-м запросе). Идемпотентен: существующие записи
/// пропускаются. Пароли DI-пользователей кейсами не используются — PasswordHash
/// кладётся заглушкой, PBKDF2 при сиде не вызывается. Копия механики зоны B-21
/// (чужая зона недоступна для ссылок — BL-001 BUG-001).
/// </summary>
public static class B19DomainSeed
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
            Content = $"Содержание DI-сида работы B-19 {semester}:{number}",
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
            FullName = $"Тестовый Студент B-19 {login}",
            Role = UserRoles.Student,
            GroupId = groupId,
            PasswordHash = "b19-di-seed-hash-not-used-by-cases",
            CreatedAt = DateTime.UtcNow,
        };
        users.Add(student);
        return student;
    }

    /// <summary>Итог сида производительности NFR-001 (кейс TS-148).</summary>
    public sealed record ReadPerformanceDataset(Guid TargetGroupId, int TargetSemester, int TargetSemesterLabCount);

    /// <summary>
    /// Датасет производительности NFR-001 (given TS-148): 200 работ, 300
    /// пользователей, группа 25 студентов × семестр 20 работ. Целевой семестр — 1
    /// (кейс запрашивает GET /api/v1/submissions?semester=1): работы с номерами
    /// 1..20; остальные 180 работ — шесть прочих семестров {2..7} по 30 работ.
    /// Целевая группа вмещает первых 25 студентов; остальные 275 — вне группы.
    /// KDF-зависимые поверхности не задействованы.
    /// </summary>
    public static ReadPerformanceDataset SeedReadPerformanceDataset(B19ApiFactory factory)
    {
        const int targetSemester = 1;
        const int targetSemesterLabs = 20;
        const int otherSemestersLabs = 30;
        const int totalStudents = 300;
        const int targetGroupSize = 25;

        // 200 работ: целевой семестр (20) + прочие семестры (6 × 30 = 180).
        for (var number = 1; number <= targetSemesterLabs; number++)
        {
            AddLab(factory, targetSemester, number);
        }

        foreach (var semester in new[] { 2, 3, 4, 5, 6, 7 })
        {
            for (var number = 1; number <= otherSemestersLabs; number++)
            {
                AddLab(factory, semester, number);
            }
        }

        // 300 пользователей, первые 25 — в целевой группе.
        var targetGroup = AddGroup(factory, "B148-Perf-Group");
        for (var i = 1; i <= totalStudents; i++)
        {
            var inTargetGroup = i <= targetGroupSize;
            AddStudent(
                factory,
                $"b148-perf-student-{i:D4}",
                inTargetGroup ? targetGroup.Id : null);
        }

        return new ReadPerformanceDataset(targetGroup.Id, targetSemester, targetSemesterLabs);
    }
}
