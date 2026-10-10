using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B21.Infrastructure;

/// <summary>
/// DI-сид доменных данных батча B-21 (given TS-193/TS-148, NFR-001): прямое наполнение
/// in-memory репозиториев из factory.Services, минуя публичный API (объёмные
/// данные готовятся в обход HTTP-лимитов; регистрационный лимитер 5/час на IP —
/// FR-004 — прервал бы засев на 6-м запросе, поэтому создание через
/// POST /auth/register недопустимо). Идемпотентен: существующие работы,
/// группы и пользователи пропускаются; записи сдач кладутся атомарным upsert
/// по паре с воспроизводимыми значениями. Пароли DI-пользователей кейсами
/// не используются — PasswordHash кладётся заглушкой, PBKDF2 при сиде
/// не вызывается.
/// </summary>
public static class B21DomainSeed
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
            Content = $"Содержание DI-сида работы B-21 {semester}:{number}",
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
            FullName = $"Тестовый Студент B-21 {login}",
            Role = UserRoles.Student,
            GroupId = groupId,
            PasswordHash = "b21-di-seed-hash-not-used-by-cases",
            CreatedAt = DateTime.UtcNow,
        };
        users.Add(student);
        return student;
    }

    /// <summary>Итог сида производительности NFR-001 (кейсы TS-193/TS-148).</summary>
    /// <param name="TargetGroupId">Id целевой группы (25 студентов).</param>
    /// <param name="TargetSemester">Целевой семестр (кейс TS-148 запрашивает semester=1).</param>
    /// <param name="TargetSemesterLabCount">Число работ целевого семестра (20).</param>
    /// <param name="TargetSemesterLabIds">Id работ целевого семестра (для контроля сдач по парам).</param>
    /// <param name="SubmittedPairCount">Число записей сдач по парам «студент группы × работа семестра» (25 × 20 = 500).</param>
    public sealed record PerformanceDataset(
        Guid TargetGroupId,
        int TargetSemester,
        int TargetSemesterLabCount,
        IReadOnlyList<Guid> TargetSemesterLabIds,
        int SubmittedPairCount);

    /// <summary>
    /// Датасет производительности NFR-001 (given TS-148, переиздание a-136 по
    /// CR-001; обязательная синхронизация зеркала B-21): 200 работ, 300
    /// пользователей, группа 25 студентов × семестр 20 работ и 500 записей
    /// сдач. Целевой семестр — 1 (кейс запрашивает
    /// GET /api/v1/submissions?groupId=…&amp;semester=1): работы с номерами
    /// 1..20; остальные 180 работ — шесть прочих семестров {2..7} по 30 работ.
    /// Целевая группа «B184-Perf-Group» вмещает первых 25 студентов; остальные
    /// 275 — вне группы. Аменда CR-001: DI-сид через ISubmissionRepository
    /// добавляет по одной записи сдачи на каждую пару «студент целевой группы ×
    /// работа целевого семестра» — 25 × 20 = 500 записей (submitDate='2026-09-15',
    /// defenseDate=null, updatedBy=null), иначе гейт p95 мерил бы
    /// недогруженный путь ведомости (выборка пар, материализация и
    /// сериализация ячеек остались бы за кадром). KDF-зависимые поверхности
    /// не задействованы.
    /// </summary>
    public static PerformanceDataset SeedPerformanceDataset(B21WebAppFactory factory)
    {
        const int targetSemester = 1;
        const int targetSemesterLabs = 20;
        const int otherSemestersLabs = 30;
        const int totalStudents = 300;
        const int targetGroupSize = 25;

        // 200 работ: целевой семестр 1 — работы №1..20 + прочие семестры
        // {2..7} по 30 = 180; вместе — ровно 200 работ (given TS-148).
        var targetLabs = new List<Lab>(targetSemesterLabs);
        for (var number = 1; number <= targetSemesterLabs; number++)
        {
            targetLabs.Add(AddLab(factory, targetSemester, number));
        }

        foreach (var semester in new[] { 2, 3, 4, 5, 6, 7 })
        {
            for (var number = 1; number <= otherSemestersLabs; number++)
            {
                AddLab(factory, semester, number);
            }
        }

        // 300 пользователей, первые 25 — в целевой группе.
        var targetGroup = AddGroup(factory, "B184-Perf-Group");
        var targetGroupStudents = new List<User>(targetGroupSize);
        for (var i = 1; i <= totalStudents; i++)
        {
            var inTargetGroup = i <= targetGroupSize;
            var student = AddStudent(
                factory,
                $"b184-perf-student-{i:D4}",
                inTargetGroup ? targetGroup.Id : null);
            if (inTargetGroup)
            {
                targetGroupStudents.Add(student);
            }
        }

        // Аменда CR-001 (переиздание a-136): 500 записей сдач — по одной на
        // каждую пару «студент целевой группы × работа целевого семестра»
        // (25 × 20), чтобы замер p95 нагружал путь ведомости. Атомарный
        // upsert по паре (IF-015) делает повторный сид идемпотентным:
        // значения дат и меток воспроизводятся.
        var submitDate = new DateOnly(2026, 9, 15);
        var seedUpdatedAt = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        var submissions = factory.Services.GetRequiredService<ISubmissionRepository>();
        foreach (var student in targetGroupStudents)
        {
            foreach (var lab in targetLabs)
            {
                submissions.Upsert(
                    student.Id,
                    lab.Id,
                    submitDate,
                    defenseDate: null,
                    updatedBy: null,
                    seedUpdatedAt);
            }
        }

        return new PerformanceDataset(
            targetGroup.Id,
            targetSemester,
            targetSemesterLabs,
            targetLabs.Select(lab => lab.Id).ToList(),
            targetGroupSize * targetSemesterLabs);
    }
}
