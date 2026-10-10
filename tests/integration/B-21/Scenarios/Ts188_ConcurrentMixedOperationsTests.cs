using System.Collections.Concurrent;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B21.Infrastructure;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B21.Scenarios;

/// <summary>
/// TS-188 (NFR-005, P0): параллельная смешанная нагрузка без исключений.
///
/// given: in-memory репозитории (FR-024); сессия teacher (uuid — автор изменений
///        сдач); подготовлены базовые лабораторные/группы/студенты (DI-сид).
/// when:  8 потоков × 200 смешанных операций (создание/чтение/обновление labs,
///        groups, students, submissions); каждый поток работает с
///        детерминированным набором собственных ключей (уникальные пары
///        (semester,number), ci-уникальные login/email/имя группы), поэтому
///        операции не конфликтуют по инвариантам.
/// then:  завершилось без исключений; инварианты уникальности не нарушены:
///        нет дублей пар (semester,number), lower(login), lower(email),
///        lower(group.name), (studentId,labId) (NFR-005; многопоточный тест).
///
/// Доработка REWORK CR-001: в смесь добавлены недостающие классы ОБНОВЛЕНИЙ,
/// поименованные в when-стадии — groups.Update (переименование в новое
/// ci-уникальное имя потока: перепривязка ci-индекса) и обновления с ИЗМЕНЕНИЕМ
/// уникального ключа (новая пара (semester,number) работы; новые ci-логин/email
/// студента) — ранее индексы не перепривязывались ни для одной сущности.
/// Доработка REWORK CR-002: добавлена ограниченная доля операций с ЗАРАНЕЕ
/// СОВПАДАЮЩИМИ ключами (шаг i=100: все потоки оспаривают одну пару
/// (semester,number), одно имя группы, один login; штатный исход проигравшей
/// попытки — StorageConflictException, в failures не фиксируется) — без них
/// пост-проверка инвариантов не могла бы отличить корректную реализацию от
/// реализации вовсе без проверки уникальности (дубли были невозможны по
/// построению).
/// </summary>
public sealed class Ts188_ConcurrentMixedOperationsTests : IClassFixture<Ts188_ConcurrentMixedOperationsTests.Fixture>
{
    private const int ThreadCount = 8;
    private const int OperationsPerThread = 200;

    /// <summary>REWORK CR-002: итерация с оспариваемыми (совпадающими у всех потоков) ключами.</summary>
    private const int ContendedStep = 100;

    /// <summary>REWORK CR-002: оспариваемая пара (semester, number) работы (вне всех собственных диапазонов).</summary>
    private const int ContendedLabSemester = 9;
    private const int ContendedLabNumber = 909;

    /// <summary>REWORK CR-002: оспариваемое ci-имя группы (вне всех собственных диапазонов).</summary>
    private const string ContendedGroupName = "B188-Contended-Group";

    /// <summary>REWORK CR-002: оспариваемый ci-логин студента (вне всех собственных диапазонов).</summary>
    private const string ContendedLogin = "b188-contended-student";

    /// <summary>
    /// REWORK CR-001: база новых номеров работ при обновлении с изменением
    /// уникального ключа (диапазон 4 000 000+t·1000+i — вне диапазонов создания
    /// 1+t·1000+i и оспариваемой пары).
    /// </summary>
    private const int LabKeyChangeBase = 4_000_000;

    private readonly Fixture _fixture;

    public Ts188_ConcurrentMixedOperationsTests(Fixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Фикстура: хост + базовые лабораторные/группы/студенты (DI-сид).</summary>
    public sealed class Fixture : IDisposable
    {
        public B21WebAppFactory Factory { get; } = new();

        public Fixture()
        {
            // Базовый набор given: лабораторные (семестр 9: 1..3), группы, студенты.
            for (var number = 1; number <= 3; number++)
            {
                B21DomainSeed.AddLab(Factory, 9, number);
            }

            B21DomainSeed.AddGroup(Factory, "B188-Base-Group-1");
            B21DomainSeed.AddGroup(Factory, "B188-Base-Group-2");
            B21DomainSeed.AddStudent(Factory, "b188-base-student-1");
            B21DomainSeed.AddStudent(Factory, "b188-base-student-2");
            B21DomainSeed.AddStudent(Factory, "b188-base-student-3");

            Assert.NotNull(
                Factory.Services.GetRequiredService<IUserRepository>().GetByLogin(SeedOptions.DefaultTeacherLogin));
        }

        public void Dispose() => Factory.Dispose();
    }

    [Fact]
    public async Task EightThreads_TwentyMixedOperationsEach_CompleteWithoutExceptions_KeepingUniquenessInvariants()
    {
        // given: in-memory репозитории; uuid teacher (автор сдач) разрешается из DI
        // (CR-003 предыдущей волны: вся нагрузка — репозиторная по верификации
        // NFR-005, HTTP-клиент не создаётся и запросов не отправляет).
        var teacher = _fixture.Factory.Services.GetRequiredService<IUserRepository>()
            .GetByLogin(SeedOptions.DefaultTeacherLogin);
        Assert.True(
            teacher is not null,
            "Предусловие given: сид-преподаватель не найден в DI-хранилище тестового хоста.");
        var teacherId = teacher!.Id;

        var failures = new ConcurrentQueue<string>();
        var barrier = new Barrier(ThreadCount);

        // Счётчики исходов оспариваемых вставок (REWORK CR-002): слоты 0/1/2 —
        // пара работы / имя группы / логин студента.
        var contendedSucceeded = new int[3];
        var contendedConflicts = new int[3];

        // when: 8 потоков × 200 смешанных операций (создание/чтение/обновление
        // labs, groups, students, submissions; итерация ContendedStep —
        // создание с заранее совпадающими ключами, REWORK CR-002).
        var workers = Enumerable.Range(0, ThreadCount)
            .Select(threadIndex => Task.Run(() =>
                RunMixedOperations(threadIndex, teacherId, barrier, failures, contendedSucceeded, contendedConflicts)))
            .ToArray();
        await Task.WhenAll(workers);

        // then (1): завершилось без исключений.
        Assert.True(
            failures.IsEmpty,
            "NFR-005: параллельная смешанная нагрузка породила исключения: "
            + Environment.NewLine
            + string.Join(Environment.NewLine, failures.Take(10)));

        // then (2): инварианты уникальности не нарушены.
        VerifyUniquenessInvariants();

        // then (3) [REWORK CR-002]: оспариваемые ключи — ровно одна запись на ключ;
        // каждый спор закончился штатно (1 успех + 7 StorageConflictException),
        // поэтому пост-проверка (2) имела реальный шанс поймать дубли.
        AssertContendedKeyOutcomes(contendedSucceeded, contendedConflicts);
    }

    /// <summary>
    /// Смешанные операции одного потока: 200 шагов; тип шага = i % 12 (0..9 — как
    /// ранее, 10 — переименование группы, 11 — обновление с изменением уникального
    /// ключа работы и студента [REWORK CR-001]); шаг ContendedStep — оспариваемые
    /// вставки [REWORK CR-002].
    /// </summary>
    private void RunMixedOperations(
        int threadIndex,
        Guid teacherId,
        Barrier barrier,
        ConcurrentQueue<string> failures,
        int[] contendedSucceeded,
        int[] contendedConflicts)
    {
        var labs = _fixture.Factory.Services.GetRequiredService<ILabRepository>();
        var groups = _fixture.Factory.Services.GetRequiredService<IGroupRepository>();
        var users = _fixture.Factory.Services.GetRequiredService<IUserRepository>();
        var submissions = _fixture.Factory.Services.GetRequiredService<ISubmissionRepository>();

        // Собственные ключи потока: базовая работа/студент для операций сдач.
        var ownLab = B21DomainSeed.AddLab(_fixture.Factory, 9, 100 + threadIndex);
        var ownStudent = B21DomainSeed.AddStudent(_fixture.Factory, $"b188-own-{threadIndex}");
        var createdLabs = new List<Lab>();
        var createdGroups = new List<Group>();
        var createdStudents = new List<User>();

        try
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));

            for (var i = 0; i < OperationsPerThread; i++)
            {
                // REWORK CR-002: ограниченная доля операций с заранее совпадающими
                // ключами — все потоки оспаривают одни и те же ключи.
                if (i == ContendedStep)
                {
                    PerformContendedAdds(labs, groups, users, contendedSucceeded, contendedConflicts);
                    continue;
                }

                switch (i % 12)
                {
                    case 0:
                    {
                        // Создание работы: пара (semester, number) уникальна в пределах прогона.
                        var lab = new Lab
                        {
                            Id = Guid.NewGuid(),
                            Semester = threadIndex + 1,
                            Number = 1 + (threadIndex * 1000) + i,
                            Content = $"B188 T{threadIndex} i{i}",
                            AssignmentUrl = null,
                            DefenseRequired = false,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                        };
                        labs.Add(lab);
                        createdLabs.Add(lab);
                        break;
                    }
                    case 1:
                    {
                        // Чтение: все работы + работа по паре.
                        _ = labs.GetAll().Count;
                        _ = labs.TryGetByPair(threadIndex + 1, 1 + (threadIndex * 1000));
                        break;
                    }
                    case 2:
                    {
                        // Обновление работы (та же пара — конфликтов нет).
                        if (createdLabs.Count > 0)
                        {
                            var source = createdLabs[^1];
                            var updated = new Lab
                            {
                                Id = source.Id,
                                Semester = source.Semester,
                                Number = source.Number,
                                Content = $"{source.Content} (обновлено {i})",
                                AssignmentUrl = source.AssignmentUrl,
                                DefenseRequired = source.DefenseRequired,
                                CreatedAt = source.CreatedAt,
                                UpdatedAt = DateTime.UtcNow,
                            };
                            labs.Update(updated);
                            createdLabs[^1] = updated;
                        }

                        break;
                    }
                    case 3:
                    {
                        // Создание группы: ci-уникальное имя.
                        var group = new Group
                        {
                            Id = Guid.NewGuid(),
                            Name = $"B188-T{threadIndex}-G{i:D3}",
                            CreatedAt = DateTime.UtcNow,
                        };
                        groups.Add(group);
                        createdGroups.Add(group);
                        break;
                    }
                    case 4:
                    {
                        // Чтение: все группы + группа по имени.
                        _ = groups.GetAll().Count;
                        if (createdGroups.Count > 0)
                        {
                            _ = groups.GetByName(createdGroups[^1].Name);
                        }

                        break;
                    }
                    case 5:
                    {
                        // Создание студента: ci-уникальные login/email.
                        var student = new User
                        {
                            Id = Guid.NewGuid(),
                            Login = $"b188-t{threadIndex}-s{i:D3}",
                            Email = $"b188-t{threadIndex}-s{i:D3}@example.com",
                            FullName = $"B188 Поток {threadIndex} Студент {i}",
                            Role = UserRoles.Student,
                            GroupId = null,
                            PasswordHash = "b188-load-hash-not-used",
                            CreatedAt = DateTime.UtcNow,
                        };
                        users.Add(student);
                        createdStudents.Add(student);
                        break;
                    }
                    case 6:
                    {
                        // Чтение: студенты + по логину.
                        _ = users.ListStudents().Count;
                        if (createdStudents.Count > 0)
                        {
                            _ = users.GetByLogin(createdStudents[^1].Login);
                        }

                        break;
                    }
                    case 7:
                    {
                        // Обновление студента (FullName; login/email не меняются).
                        if (createdStudents.Count > 0)
                        {
                            var source = createdStudents[^1];
                            var updated = new User
                            {
                                Id = source.Id,
                                Login = source.Login,
                                Email = source.Email,
                                FullName = $"{source.FullName} (обновлено {i})",
                                Role = source.Role,
                                GroupId = source.GroupId,
                                PasswordHash = source.PasswordHash,
                                CreatedAt = source.CreatedAt,
                            };
                            users.Update(updated);
                            createdStudents[^1] = updated;
                        }

                        break;
                    }
                    case 8:
                    {
                        // Сдача: upsert пары (собственный студент, собственная работа) —
                        // каждая итерация двигает дату сдачи (ветка обновления).
                        submissions.Upsert(
                            ownStudent.Id,
                            ownLab.Id,
                            new DateOnly(2026, 1, 1).AddDays(i % 28),
                            null,
                            teacherId,
                            DateTime.UtcNow);
                        break;
                    }
                    case 9:
                    {
                        // Чтение сдач: по паре и по студенту.
                        _ = submissions.GetByStudentAndLab(ownStudent.Id, ownLab.Id);
                        _ = submissions.ListByStudent(ownStudent.Id).Count;
                        break;
                    }
                    case 10:
                    {
                        // REWORK CR-001: обновление группы — переименование ранее
                        // созданной в новое ci-уникальное имя (перепривязка ci-индекса
                        // имени под конкуренцией).
                        if (createdGroups.Count > 0)
                        {
                            var source = createdGroups[^1];
                            var renamed = new Group
                            {
                                Id = source.Id,
                                Name = $"B188-T{threadIndex}-RG{i:D3}",
                                CreatedAt = source.CreatedAt,
                            };
                            groups.Update(renamed);
                            createdGroups[^1] = renamed;
                        }

                        break;
                    }
                    case 11:
                    {
                        // REWORK CR-001: обновления с ИЗМЕНЕНИЕМ уникального ключа —
                        // новая пара (semester, number) работы и новые ci-логин/email
                        // студента (перепривязка уникальных индексов под конкуренцией;
                        // значения уникальны в пределах потока и прогона).
                        if (createdLabs.Count > 0)
                        {
                            var source = createdLabs[^1];
                            var renumbered = new Lab
                            {
                                Id = source.Id,
                                Semester = source.Semester,
                                Number = LabKeyChangeBase + (threadIndex * 1000) + i,
                                Content = $"{source.Content} (пара изменена {i})",
                                AssignmentUrl = source.AssignmentUrl,
                                DefenseRequired = source.DefenseRequired,
                                CreatedAt = source.CreatedAt,
                                UpdatedAt = DateTime.UtcNow,
                            };
                            labs.Update(renumbered);
                            createdLabs[^1] = renumbered;
                        }

                        if (createdStudents.Count > 0)
                        {
                            var source = createdStudents[^1];
                            var relinked = new User
                            {
                                Id = source.Id,
                                Login = $"b188-t{threadIndex}-r{i:D3}",
                                Email = $"b188-t{threadIndex}-r{i:D3}@example.com",
                                FullName = source.FullName,
                                Role = source.Role,
                                GroupId = source.GroupId,
                                PasswordHash = source.PasswordHash,
                                CreatedAt = source.CreatedAt,
                            };
                            users.Update(relinked);
                            createdStudents[^1] = relinked;
                        }

                        break;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            failures.Enqueue(
                $"поток {threadIndex}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// REWORK CR-002: все потоки создают сущности с ОДНИМИ И ТЕМИ ЖЕ ключами.
    /// Ровно одна попытка на ключ успешна; остальные — штатный
    /// <see cref="StorageConflictException"/> (в failures НЕ фиксируется);
    /// любые иные исключения уходят в общий catch потока.
    /// </summary>
    private static void PerformContendedAdds(
        ILabRepository labs,
        IGroupRepository groups,
        IUserRepository users,
        int[] succeeded,
        int[] conflicts)
    {
        TryContendedAdd(
            0,
            () => labs.Add(new Lab
            {
                Id = Guid.NewGuid(),
                Semester = ContendedLabSemester,
                Number = ContendedLabNumber,
                Content = "B188 оспариваемая работа",
                AssignmentUrl = null,
                DefenseRequired = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }),
            succeeded,
            conflicts);

        TryContendedAdd(
            1,
            () => groups.Add(new Group
            {
                Id = Guid.NewGuid(),
                Name = ContendedGroupName,
                CreatedAt = DateTime.UtcNow,
            }),
            succeeded,
            conflicts);

        TryContendedAdd(
            2,
            () => users.Add(new User
            {
                Id = Guid.NewGuid(),
                Login = ContendedLogin,
                Email = $"{ContendedLogin}@example.com",
                FullName = "B188 Оспариваемый Студент",
                Role = UserRoles.Student,
                GroupId = null,
                PasswordHash = "b188-contended-hash-not-used",
                CreatedAt = DateTime.UtcNow,
            }),
            succeeded,
            conflicts);
    }

    /// <summary>Одна оспариваемая вставка: успех или штатный конфликт — в счётчики.</summary>
    private static void TryContendedAdd(int slot, Action add, int[] succeeded, int[] conflicts)
    {
        try
        {
            add();
            Interlocked.Increment(ref succeeded[slot]);
        }
        catch (StorageConflictException)
        {
            Interlocked.Increment(ref conflicts[slot]);
        }
    }

    /// <summary>
    /// then (3) [REWORK CR-002]: каждый спор завершился штатно — ровно одна
    /// успешная вставка и ThreadCount-1 конфликтов на ключ; в хранилище — ровно
    /// одна запись на каждый оспариваемый ключ.
    /// </summary>
    private void AssertContendedKeyOutcomes(int[] succeeded, int[] conflicts)
    {
        var keyNames = new[]
        {
            $"пара работы ({ContendedLabSemester}, {ContendedLabNumber})",
            $"имя группы «{ContendedGroupName}»",
            $"логин студента «{ContendedLogin}»",
        };

        for (var slot = 0; slot < 3; slot++)
        {
            Assert.True(
                succeeded[slot] == 1,
                $"NFR-005/REWORK CR-002: оспариваемый ключ {keyNames[slot]}: успешных вставок "
                + $"{succeeded[slot]} (ожидалась ровно 1) — составная операция "
                + "«проверка уникальности + вставка» не атомарна.");
            Assert.True(
                conflicts[slot] == ThreadCount - 1,
                $"NFR-005/REWORK CR-002: оспариваемый ключ {keyNames[slot]}: конфликтов "
                + $"{conflicts[slot]} (ожидалось {ThreadCount - 1}) — исходы спора не "
                + "сходятся (успех + StorageConflictException).");
        }

        var labs = _fixture.Factory.Services.GetRequiredService<ILabRepository>();
        var groups = _fixture.Factory.Services.GetRequiredService<IGroupRepository>();
        var users = _fixture.Factory.Services.GetRequiredService<IUserRepository>();

        var contendedLabCount = labs.GetAll()
            .Count(lab => lab.Semester == ContendedLabSemester && lab.Number == ContendedLabNumber);
        Assert.True(
            contendedLabCount == 1,
            $"NFR-005/REWORK CR-002: в хранилище {contendedLabCount} работ с оспариваемой "
            + $"парой ({ContendedLabSemester}, {ContendedLabNumber}) (ожидалась 1) — "
            + "нарушена уникальность пар (semester, number).");

        var contendedGroupCount = groups.GetAll()
            .Count(group => string.Equals(group.Name, ContendedGroupName, StringComparison.OrdinalIgnoreCase));
        Assert.True(
            contendedGroupCount == 1,
            $"NFR-005/REWORK CR-002: в хранилище {contendedGroupCount} групп с оспариваемым "
            + $"именем «{ContendedGroupName}» (ожидалась 1) — нарушена ci-уникальность имён групп.");

        var contendedStudentCount = users.ListStudents()
            .Count(student => string.Equals(student.Login, ContendedLogin, StringComparison.OrdinalIgnoreCase));
        Assert.True(
            contendedStudentCount == 1,
            $"NFR-005/REWORK CR-002: в хранилище {contendedStudentCount} студентов с оспариваемым "
            + $"логином «{ContendedLogin}» (ожидался 1) — нарушена ci-уникальность логинов.");
    }

    /// <summary>then (2): инварианты уникальности по фактическому состоянию репозиториев.</summary>
    private void VerifyUniquenessInvariants()
    {
        var labs = _fixture.Factory.Services.GetRequiredService<ILabRepository>();
        var groups = _fixture.Factory.Services.GetRequiredService<IGroupRepository>();
        var users = _fixture.Factory.Services.GetRequiredService<IUserRepository>();
        var submissions = _fixture.Factory.Services.GetRequiredService<ISubmissionRepository>();

        // Нет дублей пар (semester, number).
        var labPairs = labs.GetAll().Select(lab => (lab.Semester, lab.Number)).ToList();
        Assert.True(
            labPairs.Count == labPairs.Distinct().Count(),
            "NFR-005: нарушена уникальность пар (semester, number) работ: "
            + string.Join(", ", labPairs.GroupBy(pair => pair).Where(group => group.Count() > 1).Select(group => group.Key)));

        // Нет дублей lower(login) и lower(email).
        var students = users.ListStudents();
        var duplicateLogins = students
            .GroupBy(student => student.Login, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        Assert.True(
            duplicateLogins.Count == 0,
            "NFR-005: нарушена ci-уникальность login: " + string.Join(", ", duplicateLogins));
        var duplicateEmails = students
            .GroupBy(student => student.Email, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        Assert.True(
            duplicateEmails.Count == 0,
            "NFR-005: нарушена ci-уникальность email: " + string.Join(", ", duplicateEmails));

        // Нет дублей lower(group.name).
        var duplicateGroupNames = groups.GetAll()
            .GroupBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        Assert.True(
            duplicateGroupNames.Count == 0,
            "NFR-005: нарушена ci-уникальность имени группы: " + string.Join(", ", duplicateGroupNames));

        // Нет дублей (studentId, labId) среди всех сдач всех работ.
        var labIds = labs.GetAll().Select(lab => lab.Id).ToList();
        var submissionPairs = submissions.ListByLabIds(labIds)
            .Select(submission => (submission.StudentId, submission.LabId))
            .ToList();
        Assert.True(
            submissionPairs.Count == submissionPairs.Distinct().Count(),
            "NFR-005: нарушена уникальность пар (studentId, labId) сдач: "
            + string.Join(", ", submissionPairs.GroupBy(pair => pair).Where(group => group.Count() > 1).Select(group => group.Key)));
    }
}
