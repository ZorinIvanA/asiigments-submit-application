using System.Collections.Concurrent;
using LabsApp.Domain;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-150 «NFR-005: параллельная нагрузка 8×200 без нарушения инвариантов»
/// (concurrency, P1, NFR-005 + FR-024).
///
/// given: in-memory репозитории приложения (DI тестового хоста, FR-024);
///        8 потоков; непересекающиеся диапазоны ключей для каждого потока —
///        студенты/группы с префиксом номера потока, работы в собственном
///        семестре потока (semester = 1..8), сдачи по парам собственных
///        (studentId, labId) потока.
/// when:  200 смешанных операций на поток: создание/чтение/обновление labs,
///        groups, students, submissions (циклический скрипт длиной 200 — каждая
///        итерация: одна операция записи из смеси и контрольное чтение того же
///        ключа).
/// then:  завершение без исключений; инварианты уникальности не нарушены:
///        lower(login), lower(email), lower(group.name) уникальны, пары
///        (semester, number) и (studentId, labId) уникальны (NFR-005, методика
///        verification — многопоточный тест в dotnet test).
/// </summary>
public sealed class Ts150_Nfr005_StorageParallelStressTests : IClassFixture<B06WebAppFactory>
{
    private const int Threads = 8;
    private const int OperationsPerThread = 200;

    /// <summary>Операций создания каждого агрегата на поток: 200 / 8 доменов скрипта = 25.</summary>
    private const int EntitiesPerThread = OperationsPerThread / 8;

    private readonly B06WebAppFactory _factory;

    public Ts150_Nfr005_StorageParallelStressTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task EightThreadsByTwoHundredMixedOperations_NoExceptions_UniquenessInvariantsHold()
    {
        // given: in-memory репозитории приложения; непересекающиеся диапазоны ключей.
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var groups = _factory.Services.GetRequiredService<IGroupRepository>();
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>();
        var now = _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        var failures = new ConcurrentBag<string>();
        var allStudents = new ConcurrentBag<User>();
        var allGroups = new ConcurrentBag<Group>();
        var allLabs = new ConcurrentBag<Lab>();
        var allPairs = new ConcurrentBag<(Guid StudentId, Guid LabId)>();

        using var startGate = new ManualResetEventSlim();
        var workers = Enumerable.Range(0, Threads)
            .Select(threadIndex => Task.Run(() => RunMixedOperations(
                threadIndex,
                users,
                groups,
                labs,
                submissions,
                now,
                startGate,
                failures,
                allStudents,
                allGroups,
                allLabs,
                allPairs)))
            .ToArray();

        // when: 200 смешанных операций на поток под стартовым барьером.
        startGate.Set();
        await Task.WhenAll(workers);

        // then: завершение без исключений.
        Assert.True(
            failures.IsEmpty,
            "Потоки нагрузки завершились с отклонениями: " + string.Join(" | ", failures.Take(5)));

        // then: инвариант lower(login) и lower(email) — уникальны (Collation ci-правило).
        var storedStudents = users.ListStudents();
        Assert.Equal(Threads * EntitiesPerThread, storedStudents.Count);
        Assert.Equal(
            storedStudents.Count,
            storedStudents.Select(user => Collation.Key(user.Login)).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            storedStudents.Count,
            storedStudents.Select(user => Collation.Key(user.Email)).Distinct(StringComparer.Ordinal).Count());

        // then: инвариант lower(group.name) — уникальны.
        var storedGroups = groups.GetAll();
        Assert.Equal(Threads * EntitiesPerThread, storedGroups.Count);
        Assert.Equal(
            storedGroups.Count,
            storedGroups.Select(group => Collation.Key(group.Name)).Distinct(StringComparer.Ordinal).Count());

        // then: инвариант пар (semester, number) — уникальны; семестры потоков 1..8.
        var storedLabs = labs.GetAll();
        Assert.Equal(Threads * EntitiesPerThread, storedLabs.Count);
        Assert.Equal(
            storedLabs.Count,
            storedLabs.Select(lab => (lab.Semester, lab.Number)).Distinct().Count());
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, labs.Semesters());

        // then: инвариант пар (studentId, labId) — уникальны; у каждого студента ровно одна сдача.
        Assert.Equal(Threads * EntitiesPerThread, allPairs.Distinct().Count());
        foreach (var pair in allPairs)
        {
            Assert.NotNull(submissions.GetByStudentAndLab(pair.StudentId, pair.LabId));
        }

        foreach (var student in allStudents)
        {
            Assert.Single(submissions.ListByStudent(student.Id));
        }
    }

    /// <summary>
    /// Смешанная нагрузка одного потока: непересекающийся диапазон ключей
    /// (префикс номера потока в login/email/имени группы, собственный семестр
    /// работ), скрипт длиной 200 итераций: цикл из 8 доменов — создание
    /// студента/группы/работы/сдачи, затем их обновление; каждая запись
    /// сопровождается контрольным чтением того же ключа. Любое исключение или
    /// нарушение ожидания чтения попадает в <paramref name="failures"/>.
    /// </summary>
    private static void RunMixedOperations(
        int threadIndex,
        IUserRepository users,
        IGroupRepository groups,
        ILabRepository labs,
        ISubmissionRepository submissions,
        DateTime now,
        ManualResetEventSlim startGate,
        ConcurrentBag<string> failures,
        ConcurrentBag<User> allStudents,
        ConcurrentBag<Group> allGroups,
        ConcurrentBag<Lab> allLabs,
        ConcurrentBag<(Guid StudentId, Guid LabId)> allPairs)
    {
        try
        {
            startGate.Wait();

            var students = new List<User>(EntitiesPerThread);
            var threadGroups = new List<Group>(EntitiesPerThread);
            var threadLabs = new List<Lab>(EntitiesPerThread);

            for (var i = 0; i < OperationsPerThread; i++)
            {
                var index = i / 8;
                switch (i % 8)
                {
                    case 0: // создание студента + чтение
                        {
                            var user = new User
                            {
                                Id = Guid.NewGuid(),
                                Login = $"t150-t{threadIndex}-u{index}",
                                Email = $"t150-t{threadIndex}-u{index}@example.com",
                                PasswordHash = "t150-stress-no-kdf",
                                FullName = $"Стресс Т-150 поток {threadIndex} студент {index}",
                                Role = UserRoles.Student,
                                GroupId = null,
                                CreatedAt = now,
                            };
                            users.Add(user);
                            students.Add(user);
                            allStudents.Add(user);
                            if (users.GetByLogin(user.Login) is null)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: студент не найден сразу после создания");
                            }

                            break;
                        }

                    case 1: // создание группы + чтение
                        {
                            var group = new Group
                            {
                                Id = Guid.NewGuid(),
                                Name = $"Т-150-{threadIndex}-{index}",
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

                    case 2: // создание работы (свой семестр потока) + чтение
                        {
                            var lab = new Lab
                            {
                                Id = Guid.NewGuid(),
                                Semester = 1 + threadIndex,
                                Number = index + 1,
                                Content = $"Стресс Т-150 поток {threadIndex} работа {index}",
                                AssignmentUrl = null,
                                DefenseRequired = index % 2 == 0,
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

                    case 3: // создание сдачи по собственной паре потока + чтение
                        {
                            var pair = (students[index].Id, threadLabs[index].Id);
                            submissions.Upsert(
                                pair.Item1,
                                pair.Item2,
                                new DateOnly(2026, 9, 1).AddDays(index),
                                null,
                                null,
                                now);
                            allPairs.Add(pair);
                            if (submissions.GetByStudentAndLab(pair.Item1, pair.Item2) is null)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: сдача не найдена по паре сразу после создания");
                            }

                            break;
                        }

                    case 4: // обновление студента + чтение
                        {
                            var user = students[index];
                            var updated = new User
                            {
                                Id = user.Id,
                                Login = user.Login,
                                Email = user.Email,
                                PasswordHash = user.PasswordHash,
                                FullName = user.FullName + " (обн)",
                                Role = user.Role,
                                GroupId = user.GroupId,
                                CreatedAt = user.CreatedAt,
                            };
                            students[index] = updated;
                            users.Update(updated);
                            if (users.GetById(user.Id)?.FullName != updated.FullName)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: обновление ФИО студента не прочиталось");
                            }

                            break;
                        }

                    case 5: // обновление (переименование) группы + чтение
                        {
                            var group = threadGroups[index];
                            var renamed = new Group
                            {
                                Id = group.Id,
                                Name = group.Name + " (обн)",
                                CreatedAt = group.CreatedAt,
                            };
                            threadGroups[index] = renamed;
                            groups.Update(renamed);
                            if (groups.GetById(group.Id)?.Name != renamed.Name)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: переименование группы не прочиталось");
                            }

                            break;
                        }

                    case 6: // обновление работы + чтение
                        {
                            var lab = threadLabs[index];
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
                            threadLabs[index] = updated;
                            labs.Update(updated);
                            if (labs.GetById(lab.Id)?.Content != updated.Content)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: обновление содержания работы не прочиталось");
                            }

                            break;
                        }

                    case 7: // обновление сдачи (проставление даты защиты) + чтение
                        {
                            var pair = (students[index].Id, threadLabs[index].Id);
                            var defenseDate = new DateOnly(2026, 10, 1).AddDays(index);
                            submissions.Upsert(pair.Item1, pair.Item2, new DateOnly(2026, 9, 1).AddDays(index), defenseDate, null, now);
                            if (submissions.GetByStudentAndLab(pair.Item1, pair.Item2)?.DefenseDate != defenseDate)
                            {
                                failures.Add($"поток {threadIndex}, итерация {i}: проставленная дата защиты не прочиталась");
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
}
