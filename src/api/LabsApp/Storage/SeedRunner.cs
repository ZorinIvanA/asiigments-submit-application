using System.Globalization;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabsApp.Storage;

/// <summary>
/// Сид при старте (FR-025): (а) ровно одна учётная запись преподавателя из
/// Seed__TeacherLogin/Seed__TeacherPassword (role=teacher, GroupId=null; в Development
/// требования §8 к сид-паролю не применяются, в Production их выполнение гарантирует
/// guard FR-025 — <see cref="SeedOptionsValidator"/> валидирует дословно ту строку,
/// которую хэширует <see cref="NormalizeTeacherPassword"/>, SEC-001); (б) при <see cref="SeedOptions.ResolveDemoData"/>=true —
/// демонстрационный набор, точно воспроизводящий src/client/app/mock/seed.ts:
/// 3 группы (ИК-221/222/223), 32 студента student01..student32, 23 работы
/// (семестр 1 №1–20 и семестр 2 №1–3) и 4 сид-сдачи с UpdatedBy = uuid преподавателя.
///
/// Сид выполняется синхронно при построении приложения, до обслуживания запросов
/// (Program.cs: app.SeedDatabase()).
///
/// Идемпотентность — по естественным ключам (FR-025): пользователь — lower(login),
/// затем lower(email); группа — ci-имя; работа — пара (semester, number); сдача —
/// пара (student, lab) (Upsert). Повторный запуск и частично выполненное хранилище
/// не дублируют записи и не изменяют существующие; uuid создаются заново
/// (детерминизм НЕ требуется). Пароли созданных учёток хэшируются
/// <see cref="IPasswordHasher"/> с меткой вызывателя seed (KdfCallers.Seed, IF-002 —
/// «каждая деривация инкрементирует счётчик»): чистое хранилище с демо-набором —
/// Δkdf{seed} = 33 (32 студента + преподаватель); уже существующие учётки не
/// перехэшируются и дериваций не добавляют.
/// </summary>
public sealed class SeedRunner(
    IUserRepository users,
    IGroupRepository groups,
    ILabRepository labs,
    ISubmissionRepository submissions,
    IPasswordHasher hasher,
    IOptions<SeedOptions> seedOptions,
    IHostEnvironment environment,
    ILogger<SeedRunner> logger)
{
    /// <summary>Фиксированная метка сид-сдач — воспроизведение seed.ts (SEED_SUBMISSION_UPDATED_AT).</summary>
    private static readonly DateTime SeedSubmissionUpdatedAt = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>ФИО преподавателя сида (воспроизведение seed.ts; FR-025 не задаёт значение).</summary>
    private const string TeacherFullName = "Сидоров Семён Семёнович";

    /// <summary>Демо-пароль студентов (seed.ts: STUDENT_PASSWORD).</summary>
    private const string StudentPassword = "student123!";

    /// <summary>Домен email демо-учёток (seed.ts: teacher@example.com, studentNN@example.com).</summary>
    private const string SeedEmailDomain = "@example.com";

    private readonly IUserRepository _users = users;
    private readonly IGroupRepository _groups = groups;
    private readonly ILabRepository _labs = labs;
    private readonly ISubmissionRepository _submissions = submissions;
    private readonly IPasswordHasher _hasher = hasher;
    private readonly SeedOptions _options = seedOptions.Value;
    private readonly bool _inDevelopment = environment.IsDevelopment();
    private readonly ILogger<SeedRunner> _logger = logger;

    /// <summary>
    /// Выполняет сид. Идемпотентен по естественным ключам: каждая запись
    /// создаётся только при отсутствии своей пары/имени/логина (см. документацию класса).
    /// </summary>
    public async Task RunAsync()
    {
        var demoData = _options.ResolveDemoData(_inDevelopment);
        var teacher = await EnsureTeacherAsync(NormalizeTeacherLogin());

        if (demoData)
        {
            await SeedDemoDataAsync(teacher);
        }

        // В журнале — только счётчики (NFR-005: email/ФИО не логируются).
        _logger.LogInformation(
            "Сид выполнен (окружение {Environment}, демо-набор {DemoData}): пользователей {Users}, групп {Groups}, работ {Labs}, сдач {Submissions}.",
            environment.EnvironmentName,
            demoData,
            (await _users.ListStudentsAsync()).Count + 1,
            (await _groups.GetAllAsync()).Count,
            (await _labs.GetAllAsync()).Count,
            await CountSubmissionsAsync(demoData));
    }

    /// <summary>Логин сид-преподавателя: пустое/пробельное значение переменной → умолчание «teacher».</summary>
    private string NormalizeTeacherLogin()
    {
        var login = _options.TeacherLogin?.Trim();
        return string.IsNullOrEmpty(login) ? SeedOptions.DefaultTeacherLogin : login;
    }

    /// <summary>
    /// Пароль сид-преподавателя: пустое/пробельное значение переменной → умолчание
    /// «teacher123!» (пароль обязан быть непустым для хэширования и входа); иное
    /// значение хэшируется ДОСЛОВНО — §8 запрещает трим пароля, и Production-guard
    /// FR-025 валидирует ровно эту же строку (SEC-001: расхождение строк guard и
    /// сида позволяло пробелами обрамить слабое значение и получить учётную запись
    /// с паролем, которому проверка не предъявлялась).
    /// Требования §8 к сид-паролю в Development не применяются; в Production их
    /// выполнение гарантирует <see cref="SeedOptionsValidator"/>.
    /// </summary>
    private string NormalizeTeacherPassword()
    {
        var password = _options.TeacherPassword;
        return string.IsNullOrWhiteSpace(password) ? SeedOptions.DefaultTeacherPassword : password;
    }

    /// <summary>
    /// Преподаватель по естественному ключу: existing с этим lower(login) или, если
    /// логин свободен, с занятым сид-email (частично выполненный сид и
    /// зарегистрировавшийся ранее пользователь не должны ронять старт конфликтом
    /// уникальности); иначе — создание с ровно одной деривацией KDF (метка seed).
    /// </summary>
    private async Task<User> EnsureTeacherAsync(string login)
    {
        var email = string.Create(CultureInfo.InvariantCulture, $"{login}{SeedEmailDomain}");
        var existing = await _users.GetByLoginAsync(login) ?? await _users.GetByEmailAsync(email);
        if (existing is not null)
        {
            return existing;
        }

        var teacher = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = _hasher.Hash(NormalizeTeacherPassword(), KdfCallers.Seed),
            FullName = TeacherFullName,
            Role = UserRoles.Teacher,
            GroupId = null,
            CreatedAt = DateTime.UtcNow,
        };
        await _users.AddAsync(teacher);
        return teacher;
    }

    /// <summary>Демонстрационный набор FR-025 — воспроизведение src/client/app/mock/seed.ts.</summary>
    private async Task SeedDemoDataAsync(User teacher)
    {
        // 1. Группы: ИК-221, ИК-222, ИК-223 (ИК-223 без студентов).
        var ik221 = await EnsureGroupAsync("ИК-221");
        var ik222 = await EnsureGroupAsync("ИК-222");
        _ = await EnsureGroupAsync("ИК-223");

        // 2. Студенты 01–32: 01–25 → ИК-221, 26–30 → ИК-222, 31–32 → без группы.
        // Пароль каждого студента хэшируется отдельно (Δkdf{seed} = 32, IF-002).
        var students = new User[32];
        for (var nn = 1; nn <= students.Length; nn++)
        {
            students[nn - 1] = await EnsureStudentAsync(nn, nn <= 25 ? ik221.Id : nn <= 30 ? ik222.Id : null);
        }

        // 3. Работы: семестр 1 №1–20, затем семестр 2 №1–3; защита у чётных,
        // ссылка у кратных 5 (seed.ts: LABS_PER_SEMESTER).
        foreach (var (semester, lastNumber) in new[] { (1, 20), (2, 3) })
        {
            for (var number = 1; number <= lastNumber; number++)
            {
                await EnsureLabAsync(semester, number);
            }
        }

        // 4. Сдачи по макетам SCR-009/SCR-010 (seed.ts: SEED_SUBMISSIONS);
        // UpdatedBy — uuid преподавателя, UpdatedAt — фиксированная метка seed.ts.
        await UpsertSubmissionAsync(students[0], 1, 1, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 11), teacher);
        await UpsertSubmissionAsync(students[0], 1, 2, new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 12), teacher);
        await UpsertSubmissionAsync(students[0], 1, 3, new DateOnly(2026, 9, 3), null, teacher);
        await UpsertSubmissionAsync(students[1], 1, 1, new DateOnly(2026, 9, 1), null, teacher);
    }

    /// <summary>Группа по ci-имени либо создание (естественный ключ — имя группы).</summary>
    private async Task<Group> EnsureGroupAsync(string name)
    {
        if (await _groups.GetByNameAsync(name) is { } existing)
        {
            return existing;
        }

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTime.UtcNow,
        };
        await _groups.AddAsync(group);
        return group;
    }

    /// <summary>
    /// Студент studentNN по естественному ключу — lower(login), затем lower(email);
    /// существующая запись не изменяется (в том числе группа), иначе — создание с
    /// ровно одной деривацией KDF (метка seed).
    /// </summary>
    private async Task<User> EnsureStudentAsync(int nn, Guid? groupId)
    {
        var suffix = nn.ToString("00", CultureInfo.InvariantCulture);
        var login = $"student{suffix}";
        var email = $"student{suffix}{SeedEmailDomain}";

        var existing = await _users.GetByLoginAsync(login) ?? await _users.GetByEmailAsync(email);
        if (existing is not null)
        {
            return existing;
        }

        var student = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            FullName = $"Иванов Иван Иванович {suffix}",
            Role = UserRoles.Student,
            GroupId = groupId,
            PasswordHash = _hasher.Hash(StudentPassword, KdfCallers.Seed),
            CreatedAt = DateTime.UtcNow,
        };
        await _users.AddAsync(student);
        return student;
    }

    /// <summary>Работа по паре (semester, number) либо создание (естественный ключ — пара).</summary>
    private async Task EnsureLabAsync(int semester, int number)
    {
        if (await _labs.TryGetByPairAsync(semester, number) is not null)
        {
            return;
        }

        await _labs.AddAsync(new Lab
        {
            Id = Guid.NewGuid(),
            Semester = semester,
            Number = number,
            Content = $"Содержание лабораторной работы №{number}",
            DefenseRequired = number % 2 == 0,
            AssignmentUrl = number % 5 == 0
                ? $"https://git.example.com/assignments/{semester}/{number}"
                : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
    }

    private async Task UpsertSubmissionAsync(User student, int semester, int number, DateOnly? submitDate, DateOnly? defenseDate, User teacher)
    {
        // Fail-fast seed.ts (CR-001 T-107): тихий фолбэк labId дал бы сдачу-сироту.
        var labId = (await _labs.TryGetByPairAsync(semester, number))?.Id
            ?? throw new InvalidOperationException(
                $"Работы {semester}:{number} нет в таблице работ — правило сид-сдачи невыполнимо.");

        var stored = await _submissions.UpsertAsync(student.Id, labId, submitDate, defenseDate, teacher.Id, SeedSubmissionUpdatedAt);

        if (stored is null)
        {
            throw new InvalidOperationException($"Сид-сдача (студент {student.Login}, работа {semester}:{number}) не сохранилась.");
        }
    }

    private async Task<int> CountSubmissionsAsync(bool demoData) =>
        demoData
            ? (await _submissions.ListByLabIdsAsync(
                (await _labs.GetAllAsync()).Select(lab => lab.Id).ToList())).Count
            : 0;
}
