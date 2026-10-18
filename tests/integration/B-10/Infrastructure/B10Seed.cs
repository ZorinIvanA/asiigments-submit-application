using System.Globalization;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>Запись сдачи в ведомости (табличная проекция, достаточная кейсу TS-120).</summary>
public sealed record GridSubmission(string StudentId, string LabId);

/// <summary>
/// DI-сид и помощники батча B-10 (ADR-010, CR-001): прямое наполнение/чтение
/// репозиториев тестового хоста БЕЗ демо-набора FR-004 (умолчание харнесов
/// Seed__DemoData=false — ADR-010/NFR-011) для шагов given (лабы, студенты,
/// группы, сдачи) и инспекции хранилища. Используются только публичные интерфейсы
/// IF-015 — код реализации не затрагивается. Обращение к factory.Services
/// материализует хост (сид выполняется при построении приложения до первого
/// запроса; учётка teacher создаётся сидом из Seed__* независимо от демо-набора).
/// </summary>
public static class B10Seed
{
    /// <summary>Логин DI-сид-студента для сессий student (TS-121/TS-122).</summary>
    public const string StudentLogin = "b10student";

    /// <summary>Добавляет студента напрямую в IUserRepository (сессия будет минтиться, ADR-022).</summary>
    public static User AddStudent(B10HostFactory factory, string login, Guid? groupId = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = string.Create(CultureInfo.InvariantCulture, $"{login}@example.com"),
            // Сессии минтятся через ITokenService (ADR-022) — пароль не используется.
            PasswordHash = "di-seed-mint-only",
            FullName = "Батч Десять Студентович",
            Role = UserRoles.Student,
            GroupId = groupId,
            CreatedAt = DateTime.UtcNow,
        };
        factory.Services.GetRequiredService<IUserRepository>().Add(user);
        return user;
    }

    /// <summary>Добавляет группу напрямую в IGroupRepository (шаг given «группа известна», TS-120).</summary>
    public static Group AddGroup(B10HostFactory factory, string name)
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

    /// <summary>Добавляет работу напрямую в ILabRepository (DI-сид кейса, ADR-010).</summary>
    public static Lab AddLab(
        B10HostFactory factory,
        int semester,
        int number,
        string? content = null,
        string? assignmentUrl = null,
        bool defenseRequired = false)
    {
        var lab = new Lab
        {
            Id = Guid.NewGuid(),
            Semester = semester,
            Number = number,
            Content = content ?? string.Create(CultureInfo.InvariantCulture, $"Содержание (DI-сид) {semester}:{number}"),
            AssignmentUrl = assignmentUrl,
            DefenseRequired = defenseRequired,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        factory.Services.GetRequiredService<ILabRepository>().Add(lab);
        return lab;
    }

    /// <summary>Добавляет сдачу напрямую в ISubmissionRepository (шаг given «сдачи студентов», TS-120).</summary>
    public static Submission AddSubmission(
        B10HostFactory factory,
        Guid studentId,
        Guid labId,
        DateOnly? submitDate,
        DateOnly? defenseDate)
    {
        var stored = factory.Services.GetRequiredService<ISubmissionRepository>()
            .Upsert(studentId, labId, submitDate, defenseDate, updatedBy: null, updatedAt: DateTime.UtcNow);
        return stored
            ?? throw new InvalidOperationException(
                "Сид: сдача не сохранилась — репозиторий вернул null (обе даты null означают отсутствие записи).");
    }

    /// <summary>Общее число работ в хранилище — шаг given «в хранилище есть лабораторные».</summary>
    public static int TotalLabCount(B10HostFactory factory) =>
        factory.Services.GetRequiredService<ILabRepository>().GetAll().Count;

    /// <summary>Сдачи работы напрямую из ISubmissionRepository (инспекция каскада, TS-120).</summary>
    public static IReadOnlyList<Submission> SubmissionsByLab(B10HostFactory factory, Guid labId) =>
        factory.Services.GetRequiredService<ISubmissionRepository>().ListByLabIds(new[] { labId });

    /// <summary>
    /// Все записи ведомости группы за семестр: GET /api/v1/submissions читается
    /// постранично до страницы без студентов (зеркало помощника зоны B-04;
    /// контракт проекции: submissions[{studentId, labId, …}], students[]).
    /// </summary>
    public static async Task<IReadOnlyList<GridSubmission>> ReadAllGridSubmissionsAsync(
        HttpClient teacherClient, Guid groupId, int semester)
    {
        var result = new List<GridSubmission>();
        var page = 1;
        while (page <= 1000)
        {
            using var response = await teacherClient.GetAsync(
                $"/api/v1/submissions?groupId={Uri.EscapeDataString(groupId.ToString())}" +
                $"&semester={semester.ToString(CultureInfo.InvariantCulture)}" +
                $"&page={page.ToString(CultureInfo.InvariantCulture)}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            foreach (var submission in root.GetProperty("submissions").EnumerateArray())
            {
                result.Add(new GridSubmission(
                    StudentId: submission.GetProperty("studentId").GetString()!,
                    LabId: submission.GetProperty("labId").GetString()!));
            }

            if (root.GetProperty("students").GetArrayLength() == 0)
            {
                return result;
            }

            page++;
        }

        throw new InvalidOperationException(
            "Ведомость не заканчивается за 1000 страниц — подозрение на некорректную пагинацию.");
    }
}
