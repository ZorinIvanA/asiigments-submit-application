using System.Collections.Concurrent;
using LabsApp.Domain;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-150 «NFR-005: параллельная нагрузка 8×200 с оспариваемыми ключами — без
/// исключений и дублей» (concurrency, P1, NFR-005 + FR-024 AC «Атомарность
/// уникальности» — все пять инвариантов).
///
/// given: in-memory репозитории приложения (DI тестового хоста, FR-024), ПРЯМЫЕ
///        вызовы интерфейсов IUserRepository/IGroupRepository/ILabRepository/
///        ISubmissionRepository (методика verification NFR-005 — многопоточный
///        тест в dotnet test, без HTTP-слоя и лимитеров); заранее созданы студент
///        (якорный) и работа (semester=9, number=8); 8 потоков × 200 смешанных
///        операций: ДОЛЯ BULK — непересекающиеся диапазоны естественных ключей на
///        поток (префикс номера потока в login/email/имени группы, собственный
///        семестр работ 1..8), ДОЛЯ КОНКУРСНАЯ — общие оспариваемые ключи, на
///        которых все потоки конкурируют созданием/апсертом через методы create,
///        инкапсулирующие проверку уникальности: пользователь с
///        lower(login)='stress-login' (email у каждого потока свой
///        'stress-t{t}@example.com'), пользователь с lower(email)='stress@example.com'
///        (login у каждого потока свой 'stress-u{t}'), группа с
///        lower(name)='стресс-группа', работа с парой (semester=9, number=9),
///        upsert сдачи якорной пары (studentId, labId (9,8)) с разными датами от
///        каждого потока (РЕВ-ISS-006: только пересекающиеся ключи делают
///        проверку инвариантов фальсифицирующей — при непересекающихся диапазонах
///        нарушение атомарности ненаблюдаемо по построению).
/// when:  параллельный прогон 8 потоков (bulk-операции + конкурсные
///        создания/апсерты на общих ключах) под стартовым барьером; после
///        завершения всех потоков — инспекция хранилищ по каждому оспариваемому
///        ключу.
/// then:  завершение без исключений; по каждому оспариваемому ключу РОВНО ОДНА
///        запись (пользователь lower(login)='stress-login'; пользователь
///        lower(email)='stress@example.com'; группа 'стресс-группа'; работа (9,9);
///        сдача якорной пары — дата равна значению одного из конкурентных
///        апсертов); отклонённые конкурсом создания не оставляют частичных
///        записей; bulk-ключи — по одной записи каждый.
/// </summary>
public sealed class Ts150_Nfr005_StorageParallelStressTests : IClassFixture<B07WebAppFactory>
{
    private const int Threads = 8;
    private const int OperationsPerThread = 200;

    /// <summary>Операций создания каждого агрегата на поток: 200 / 10 шагов скрипта = 20.</summary>
    private const int EntitiesPerThread = OperationsPerThread / 10;

    /// <summary>Оспариваемый естественный ключ пользователя: lower(login).</summary>
    private const string ContestedLogin = "stress-login";

    /// <summary>Оспариваемый естественный ключ пользователя: lower(email).</summary>
    private const string ContestedEmail = "stress@example.com";

    /// <summary>Оспариваемый естественный ключ группы: lower(name).</summary>
    private const string ContestedGroupName = "стресс-группа";

    /// <summary>Оспариваемая пара работы.</summary>
    private const int ContestedSemester = 9;
    private const int ContestedNumber = 9;

    private readonly B07WebAppFactory _factory;

    public Ts150_Nfr005_StorageParallelStressTests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task EightThreadsByTwoHundredMixedOperations_NoExceptions_ContestedKeysHaveExactlyOneRecord()
    {
        // given: in-memory репозитории приложения; якорные записи конкурсной доли.
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var groups = _factory.Services.GetRequiredService<IGroupRepository>();
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>();
        var now = _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        var anchorStudent = NewStudent(
            "t150-anchor-student",
            "t150-anchor@example.com",
            "Стресс Т-150 якорный студент",
            now);
        users.Add(anchorStudent);

        var anchorLab = new Lab
        {
            Id = Guid.NewGuid(),
            Semester = 9,
            Number = 8,
            Content = "Стресс Т-150 якорная работа (9,8)",
            AssignmentUrl = null,
            DefenseRequired = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        labs.Add(anchorLab);

        var failures = new ConcurrentBag<string>();
        var allStudents = new ConcurrentBag<User>();
        var allGroups = new ConcurrentBag<Group>();
        var allLabs = new ConcurrentBag<Lab>();
        var allBulkPairs = new ConcurrentBag<(Guid StudentId, Guid LabId)>();

        // Победители конкурсных созданий: ровно один поток занимает каждый ключ.
        var loginKeyWinners = new ConcurrentBag<int>();
        var emailKeyWinners = new ConcurrentBag<int>();
        var groupNameWinners = new ConcurrentBag<int>();
        var labPairWinners = new ConcurrentBag<int>();

        using var startGate = new ManualResetEventSlim();
        var workers = Enumerable.Range(0, Threads)
            .Select(threadIndex => Task.Run(() => RunMixedOperations(
                threadIndex,
                users,
                groups,
                labs,
                submissions,
                now,
                anchorStudent.Id,
                anchorLab.Id,
                startGate,
                failures,
                allStudents,
                allGroups,
                allLabs,
                allBulkPairs,
                loginKeyWinners,
                emailKeyWinners,
                groupNameWinners,
                labPairWinners)))
            .ToArray();

        // when: 200 смешанных операций на поток (bulk + конкурсные) под стартовым барьером.
        startGate.Set();
        await Task.WhenAll(workers);

        // then: завершение без исключений.
        Assert.True(
            failures.IsEmpty,
            "Потоки нагрузки завершились с отклонениями: " + string.Join(" | ", failures.Take(5)));

        // then: каждый оспариваемый ключ создания занят РОВНО ОДНИМ потоком
        // (остальные попытки отклонены StorageConflictException — учтены ниже по
        // состоянию хранилищ).
        Assert.Single(loginKeyWinners);
        Assert.Single(emailKeyWinners);
        Assert.Single(groupNameWinners);
        Assert.Single(labPairWinners);

        // then: инспекция хранилищ по оспариваемому ключу lower(login)='stress-login':
        // ровно одна запись; отклонённые конкурсом создания не оставили частичных
        // записей — email 'stress-t{t}@example.com' существует только у победителя.
        var storedStudents = users.ListStudents();
        var loginKeyUser = Assert.Single(storedStudents, user => Collation.Key(user.Login) == ContestedLogin);
        Assert.Equal(1, storedStudents.Count(user => Collation.Key(user.Email).StartsWith("stress-t", StringComparison.Ordinal)));
        Assert.Equal(ContestedLogin, Collation.Key(loginKeyUser.Login));
        Assert.True(
            Collation.Key(loginKeyUser.Email).StartsWith("stress-t", StringComparison.Ordinal),
            "Победитель ключа lower(login)='stress-login' обязан иметь email 'stress-t{поток}@example.com'.");

        // then: инспекция оспариваемого ключа lower(email)='stress@example.com':
        // ровно одна запись; логины 'stress-u{t}' — только у победителя.
        var emailKeyUser = Assert.Single(storedStudents, user => Collation.Key(user.Email) == ContestedEmail);
        Assert.Equal(1, storedStudents.Count(user => Collation.Key(user.Login).StartsWith("stress-u", StringComparison.Ordinal)));
        Assert.Equal(ContestedEmail, Collation.Key(emailKeyUser.Email));
        Assert.True(
            Collation.Key(emailKeyUser.Login).StartsWith("stress-u", StringComparison.Ordinal),
            "Победитель ключа lower(email)='stress@example.com' обязан иметь логин 'stress-u{поток}'.");

        // then: инспекция оспариваемого ключа lower(group.name)='стресс-группа' — ровно одна запись.
        var storedGroups = groups.GetAll();
        Assert.Single(storedGroups, group => Collation.Key(group.Name) == ContestedGroupName);

        // then: инспекция оспариваемой пары работы (9,9) — ровно одна запись.
        var storedLabs = labs.GetAll();
        Assert.Single(storedLabs, lab => lab.Semester == ContestedSemester && lab.Number == ContestedNumber);

        // then: инспекция конкурсного апсерта якорной пары (studentId, labId (9,8)):
        // ровно одна запись сдачи, дата — значение ОДНОГО из конкурентных апсертов.
        var proposedDates = Enumerable.Range(0, Threads)
            .Select(threadIndex => new DateOnly(2026, 9, 1).AddDays(threadIndex))
            .ToList();
        var anchorSubmission = submissions.GetByStudentAndLab(anchorStudent.Id, anchorLab.Id);
        Assert.NotNull(anchorSubmission);
        Assert.Single(submissions.ListByStudent(anchorStudent.Id));
        Assert.Single(submissions.ListByLabIds(new[] { anchorLab.Id }));
        Assert.True(
            anchorSubmission!.SubmitDate is { } submitDate && proposedDates.Contains(submitDate),
            $"Дата сдачи якорной пары обязана совпадать с датой ОДНОГО из конкурентных апсертов, фактически {anchorSubmission.SubmitDate?.ToString() ?? "<null>"}.");

        // then: bulk-ключи — по одной записи каждый (инварианты уникальности NFR-005).
        var bulkStudents = storedStudents.Count - 3; // якорный студент + два победителя конкурсных ключей
        Assert.Equal(Threads * EntitiesPerThread, bulkStudents);
        Assert.Equal(
            storedStudents.Count,
            storedStudents.Select(user => Collation.Key(user.Login)).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            storedStudents.Count,
            storedStudents.Select(user => Collation.Key(user.Email)).Distinct(StringComparer.Ordinal).Count());

        Assert.Equal((Threads * EntitiesPerThread) + 1, storedGroups.Count);
        Assert.Equal(
            storedGroups.Count,
            storedGroups.Select(group => Collation.Key(group.Name)).Distinct(StringComparer.Ordinal).Count());

        Assert.Equal((Threads * EntitiesPerThread) + 2, storedLabs.Count); // + якорная (9,8) и оспариваемая (9,9)
        Assert.Equal(
            storedLabs.Count,
            storedLabs.Select(lab => (lab.Semester, lab.Number)).Distinct().Count());
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, labs.Semesters());

        // then: инвариант пар (studentId, labId) — уникальны; у каждого bulk-студента
        // ровно одна сдача, по каждой bulk-паре запись находится.
        Assert.Equal(Threads * EntitiesPerThread, allBulkPairs.Distinct().Count());
        foreach (var pair in allBulkPairs)
        {
            Assert.NotNull(submissions.GetByStudentAndLab(pair.StudentId, pair.LabId));
        }

        foreach (var student in allStudents)
        {
            Assert.Single(submissions.ListByStudent(student.Id));
        }
    }

    /// <summary>
    /// Смешанная нагрузка одного потока: скрипт длиной 200 операций, шаг = i % 10.
    /// Шаги 0–7 — bulk-доля (непересекающиеся диапазоны ключей потока: создание
    /// студента/группы/работы/сдачи и их обновление); шаги 8–9 — конкурсная доля
    /// на общих оспариваемых ключах (по циклу: ключи lower(login)/lower(email)/
    /// lower(group.name)/(semester, number); шаг 9 — апсерт якорной пары сдачи с
    /// датой потока). Каждая запись сопровождается контрольным чтением того же
    /// ключа. StorageConflictException конкурсного создания — ожидаемое отклонение
    /// (проигрыш гонки за ключ); любое другое исключение или нарушение ожидания
    /// чтения попадает в <paramref name="failures"/>.
    /// </summary>
    private static void RunMixedOperations(
        int threadIndex,
        IUserRepository users,
        IGroupRepository groups,
        ILabRepository labs,
        ISubmissionRepository submissions,
        DateTime now,
        Guid anchorStudentId,
        Guid anchorLabId,
        ManualResetEventSlim startGate,
        ConcurrentBag<string> failures,
        ConcurrentBag<User> allStudents,
        ConcurrentBag<Group> allGroups,
        ConcurrentBag<Lab> allLabs,
        ConcurrentBag<(Guid StudentId, Guid LabId)> allBulkPairs,
        ConcurrentBag<int> loginKeyWinners,
        ConcurrentBag<int> emailKeyWinners,
        ConcurrentBag<int> groupNameWinners,
        ConcurrentBag<int> labPairWinners)
    {
        try
        {
            startGate.Wait();

            var students = new List<User>(EntitiesPerThread);
            var threadGroups = new List<Group>(EntitiesPerThread);
            var threadLabs = new List<Lab>(EntitiesPerThread);
            var threadDate = new DateOnly(2026, 9, 1).AddDays(threadIndex);

            for (var i = 0; i < OperationsPerThread; i++)
            {
                var cycle = i / 10;
                switch (i % 10)
                {
                    case 0: // bulk: создание студента + чтение
                        {
                            var user = NewStudent(
                                $"t150-t{threadIndex}-u{cycle}",
                                $"t150-t{threadIndex}-u{cycle}@example.com",
                                $"Стресс Т-150 поток {threadIndex} студент {cycle}",
                                now);
                            users.Add(user);
                            students.Add(user);
                            allStudents.Add(user);
                            if (users.GetByLogin(user.Login) is null)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: студент не найден сразу после создания");
                            }

                            break;
                        }

                    case 1: // bulk: создание группы + чтение
                        {
                            var group = new Group
                            {
                                Id = Guid.NewGuid(),
                                Name = $"Т-150-{threadIndex}-{cycle}",
                                CreatedAt = now,
                            };
                            groups.Add(group);
                            threadGroups.Add(group);
                            allGroups.Add(group);
                            if (groups.GetByName(group.Name) is null)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: группа не найдена сразу после создания");
                            }

                            break;
                        }

                    case 2: // bulk: создание работы (свой семестр потока 1..8) + чтение
                        {
                            var lab = new Lab
                            {
                                Id = Guid.NewGuid(),
                                Semester = 1 + threadIndex,
                                Number = cycle + 1,
                                Content = $"Стресс Т-150 поток {threadIndex} работа {cycle}",
                                AssignmentUrl = null,
                                DefenseRequired = cycle % 2 == 0,
                                CreatedAt = now,
                                UpdatedAt = now,
                            };
                            labs.Add(lab);
                            threadLabs.Add(lab);
                            allLabs.Add(lab);
                            if (labs.TryGetByPair(lab.Semester, lab.Number) is null)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: работа не найдена по паре сразу после создания");
                            }

                            break;
                        }

                    case 3: // bulk: создание сдачи по собственной паре потока + чтение
                        {
                            var pair = (students[cycle].Id, threadLabs[cycle].Id);
                            submissions.Upsert(pair.Item1, pair.Item2, threadDate.AddDays(cycle), null, null, now);
                            allBulkPairs.Add(pair);
                            if (submissions.GetByStudentAndLab(pair.Item1, pair.Item2) is null)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: сдача не найдена по паре сразу после создания");
                            }

                            break;
                        }

                    case 4: // bulk: обновление студента + чтение
                        {
                            var user = students[cycle];
                            var updated = NewStudent(user.Login, user.Email, user.FullName + " (обн)", user.CreatedAt);
                            updated.Id = user.Id;
                            students[cycle] = updated;
                            users.Update(updated);
                            if (users.GetById(user.Id)?.FullName != updated.FullName)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: обновление ФИО студента не прочиталось");
                            }

                            break;
                        }

                    case 5: // bulk: обновление (переименование) группы + чтение
                        {
                            var group = threadGroups[cycle];
                            var renamed = new Group
                            {
                                Id = group.Id,
                                Name = group.Name + " (обн)",
                                CreatedAt = group.CreatedAt,
                            };
                            threadGroups[cycle] = renamed;
                            groups.Update(renamed);
                            if (groups.GetById(group.Id)?.Name != renamed.Name)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: переименование группы не прочиталось");
                            }

                            break;
                        }

                    case 6: // bulk: обновление работы + чтение
                        {
                            var lab = threadLabs[cycle];
                            var updated = new Lab
                            {
                                Id = lab.Id,
                                Semester = lab.Semester,
                                Number = lab.Number,
                                Content = lab.Content + " (обн)",
                                AssignmentUrl = lab.AssignmentUrl,
                                DefenseRequired = lab.DefenseRequired,
                                CreatedAt = lab.CreatedAt,
                                UpdatedAt = now,
                            };
                            threadLabs[cycle] = updated;
                            labs.Update(updated);
                            if (labs.GetById(lab.Id)?.Content != updated.Content)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: обновление содержания работы не прочиталось");
                            }

                            break;
                        }

                    case 7: // bulk: обновление сдачи (проставление даты защиты) + чтение
                        {
                            var pair = (students[cycle].Id, threadLabs[cycle].Id);
                            var defenseDate = new DateOnly(2026, 10, 1).AddDays(cycle);
                            submissions.Upsert(pair.Item1, pair.Item2, threadDate.AddDays(cycle), defenseDate, null, now);
                            if (submissions.GetByStudentAndLab(pair.Item1, pair.Item2)?.DefenseDate != defenseDate)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: проставленная дата защиты не прочиталась");
                            }

                            break;
                        }

                    case 8: // конкурсная доля: создание на общем оспариваемом ключе (ключ — по циклу)
                        {
                            switch (cycle % 4)
                            {
                                case 0: // оспариваемый ключ lower(login)='stress-login'
                                    try
                                    {
                                        users.Add(NewStudent(
                                            ContestedLogin,
                                            $"stress-t{threadIndex}@example.com",
                                            $"Стресс Т-150 поток {threadIndex} ключ login",
                                            now));
                                        loginKeyWinners.Add(threadIndex);
                                    }
                                    catch (StorageConflictException)
                                    {
                                        // Ожидаемое отклонение конкурсом — ключ занят другим потоком.
                                    }

                                    break;
                                case 1: // оспариваемый ключ lower(email)='stress@example.com'
                                    try
                                    {
                                        users.Add(NewStudent(
                                            $"stress-u{threadIndex}",
                                            ContestedEmail,
                                            $"Стресс Т-150 поток {threadIndex} ключ email",
                                            now));
                                        emailKeyWinners.Add(threadIndex);
                                    }
                                    catch (StorageConflictException)
                                    {
                                        // Ожидаемое отклонение конкурсом.
                                    }

                                    break;
                                case 2: // оспариваемый ключ lower(group.name)='стресс-группа'
                                    try
                                    {
                                        groups.Add(new Group
                                        {
                                            Id = Guid.NewGuid(),
                                            Name = ContestedGroupName,
                                            CreatedAt = now,
                                        });
                                        groupNameWinners.Add(threadIndex);
                                    }
                                    catch (StorageConflictException)
                                    {
                                        // Ожидаемое отклонение конкурсом.
                                    }

                                    break;
                                default: // оспариваемая пара работы (semester=9, number=9)
                                    try
                                    {
                                        labs.Add(new Lab
                                        {
                                            Id = Guid.NewGuid(),
                                            Semester = ContestedSemester,
                                            Number = ContestedNumber,
                                            Content = "Стресс Т-150 оспариваемая работа (9,9)",
                                            AssignmentUrl = null,
                                            DefenseRequired = false,
                                            CreatedAt = now,
                                            UpdatedAt = now,
                                        });
                                        labPairWinners.Add(threadIndex);
                                    }
                                    catch (StorageConflictException)
                                    {
                                        // Ожидаемое отклонение конкурсом.
                                    }

                                    break;
                            }

                            break;
                        }

                    default: // конкурсная доля: апсерт якорной пары сдачи с датой потока + чтение
                        {
                            submissions.Upsert(anchorStudentId, anchorLabId, threadDate, null, null, now);
                            if (submissions.GetByStudentAndLab(anchorStudentId, anchorLabId) is null)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: якорная сдача не найдена после апсерта");
                            }

                            break;
                        }
                }
            }
        }
        catch (Exception exception)
        {
            failures.Add($"поток {threadIndex}: {exception.GetType().FullName}: {exception.Message}");
        }
    }

    private static User NewStudent(string login, string email, string fullName, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        Login = login,
        Email = email,
        PasswordHash = "t150-stress-no-kdf",
        FullName = fullName,
        Role = UserRoles.Student,
        GroupId = null,
        CreatedAt = createdAt,
    };
}
