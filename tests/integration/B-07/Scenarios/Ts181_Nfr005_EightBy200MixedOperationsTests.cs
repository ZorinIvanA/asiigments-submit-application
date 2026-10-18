using System.Collections.Concurrent;
using LabsApp.Domain;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-181 «NFR-005: 8 потоков × 200 смешанных операций без исключений»
/// (concurrency, P1, NFR-005 + FR-024).
///
/// given: in-memory репозитории приложения (DI тестового хоста, FR-024);
///        подготовлена смесь операций создания/чтения/обновления labs, groups,
///        students, submissions; для созданий зарезервированы
///        непересекающиеся уникальные ключи: у потока t — собственный семестр
///        работ (100+t) с номерами 0..19 (пары (semester,number)), логины
///        't181-u{t}-{k}' с email 't181-u{t}-{k}@example.com', имена групп
///        'Т181-T{t}-Группа-{k}' (и переименованные '-ren'), пары
///        (student, lab) — только из собственных сущностей потока.
/// when:  8 потоков × 200 смешанных операций параллельно (стартовый барьер).
/// then:  завершено без исключений; инварианты уникальности не нарушены:
///        нет дублей (semester,number), lower(login), lower(name группы),
///        (studentId,labId) (NFR-005 constraint + verification дословно —
///        многопоточный тест в dotnet test).
/// </summary>
public sealed class Ts181_Nfr005_EightBy200MixedOperationsTests : IClassFixture<B07WebAppFactory>
{
    private const int Threads = 8;
    private const int OperationsPerThread = 200;

    /// <summary>Полных циклов (блоков по 10 операций) на поток: 200 / 10 = 20.</summary>
    private const int CyclesPerThread = OperationsPerThread / 10;

    /// <summary>Собственный семестр потока t: пары (100+t, k) не пересекаются между потоками.</summary>
    private static int ThreadSemester(int threadIndex) => 100 + threadIndex;

    private readonly B07WebAppFactory _factory;

    public Ts181_Nfr005_EightBy200MixedOperationsTests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task EightThreadsBy200MixedOperations_NoExceptions_NoDuplicateInvariants()
    {
        // given: in-memory репозитории приложения; пустые хранилища (кроме
        // сид-преподавателя — не студент, инвариантам кейса не мешает).
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var groups = _factory.Services.GetRequiredService<IGroupRepository>();
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>();
        var now = _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        var failures = new ConcurrentBag<string>();
        var studentIds = new ConcurrentBag<Guid>();
        var groupIds = new ConcurrentBag<Guid>();
        var labIds = new ConcurrentBag<Guid>();
        var submissionPairs = new ConcurrentBag<(Guid StudentId, Guid LabId)>();

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
                studentIds,
                groupIds,
                labIds,
                submissionPairs)))
            .ToArray();

        // when: 8 потоков × 200 смешанных операций параллельно.
        startGate.Set();
        await Task.WhenAll(workers);

        // then: завершено без исключений (ключи непересекающиеся — конфликтов
        // StorageConflictException быть не должно вовсе).
        Assert.True(
            failures.IsEmpty,
            "Потоки нагрузки завершились с исключениями/отклонениями: " + string.Join(" | ", failures.Take(5)));

        // then: нет дублей (semester, number) — у каждой работы уникальная пара.
        var storedLabs = labs.GetAll();
        Assert.Equal(Threads * CyclesPerThread, storedLabs.Count);
        Assert.Equal(
            storedLabs.Count,
            storedLabs.Select(lab => (lab.Semester, lab.Number)).Distinct().Count());

        // then: нет дублей lower(login) — у каждого студента уникальный ci-логин.
        var storedStudents = users.ListStudents();
        Assert.Equal(Threads * CyclesPerThread, storedStudents.Count);
        Assert.Equal(
            storedStudents.Count,
            storedStudents.Select(user => Collation.Key(user.Login)).Distinct(StringComparer.Ordinal).Count());

        // then: нет дублей lower(name группы) — у каждой группы уникальное ci-имя.
        var storedGroups = groups.GetAll();
        Assert.Equal(Threads * CyclesPerThread, storedGroups.Count);
        Assert.Equal(
            storedGroups.Count,
            storedGroups.Select(group => Collation.Key(group.Name)).Distinct(StringComparer.Ordinal).Count());

        // then: нет дублей (studentId, labId) — у каждой сдачи уникальная пара;
        // у каждого студента ровно одна сдача, пара находится прямым чтением.
        var storedSubmissions = storedStudents
            .SelectMany(student => submissions.ListByStudent(student.Id))
            .ToList();
        Assert.Equal(Threads * CyclesPerThread, storedSubmissions.Count);
        Assert.Equal(
            storedSubmissions.Count,
            storedSubmissions.Select(submission => (submission.StudentId, submission.LabId)).Distinct().Count());
        Assert.Equal(
            Threads * CyclesPerThread,
            submissionPairs.Distinct().Count(pair => submissions.GetByStudentAndLab(pair.StudentId, pair.LabId) is not null));
    }

    /// <summary>
    /// Смешанная нагрузка одного потока: 200 операций, скрипт шага i % 10 в цикле
    /// k = i / 10. Шаги 0–3 — создания (студент, группа, работа, сдача собственной
    /// пары), шаг 4 — контрольные чтения созданного, шаги 5–8 — обновления
    /// (работа, группа, студент SetGroup, сдача — новые даты), шаг 9 — смешанные
    /// чтения списков. Ключи потока непересекающиеся, поэтому ЛЮБОЕ исключение —
    /// нарушение NFR-005 и попадает в <paramref name="failures"/>.
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
        ConcurrentBag<Guid> studentIds,
        ConcurrentBag<Guid> groupIds,
        ConcurrentBag<Guid> labIds,
        ConcurrentBag<(Guid StudentId, Guid LabId)> submissionPairs)
    {
        try
        {
            startGate.Wait();

            var ownStudents = new Guid[CyclesPerThread];
            var ownGroups = new Guid[CyclesPerThread];
            var ownLabs = new Guid[CyclesPerThread];

            for (var i = 0; i < OperationsPerThread; i++)
            {
                var cycle = i / 10;
                switch (i % 10)
                {
                    case 0:
                    {
                        // Создание студента потока (непересекающийся login/email).
                        var student = new User
                        {
                            Id = Guid.NewGuid(),
                            Login = $"t181-u{threadIndex}-{cycle:00}",
                            Email = $"t181-u{threadIndex}-{cycle:00}@example.com",
                            PasswordHash = $"t181-stub-hash-{threadIndex}-{cycle}",
                            FullName = $"Стресс Т-181 поток {threadIndex} студент {cycle}",
                            Role = UserRoles.Student,
                            GroupId = null,
                            CreatedAt = now,
                        };
                        users.Add(student);
                        ownStudents[cycle] = student.Id;
                        studentIds.Add(student.Id);
                        Assert.NotNull(users.GetById(student.Id));
                        break;
                    }

                    case 1:
                    {
                        // Создание группы потока (непересекающееся ci-имя).
                        var group = new Group
                        {
                            Id = Guid.NewGuid(),
                            Name = $"Т181-T{threadIndex}-Группа-{cycle:00}",
                            CreatedAt = now,
                        };
                        groups.Add(group);
                        ownGroups[cycle] = group.Id;
                        groupIds.Add(group.Id);
                        Assert.NotNull(groups.GetByName(group.Name));
                        break;
                    }

                    case 2:
                    {
                        // Создание работы потока (непересекающаяся пара (100+t, k)).
                        var lab = new Lab
                        {
                            Id = Guid.NewGuid(),
                            Semester = ThreadSemester(threadIndex),
                            Number = cycle,
                            Content = $"Стресс Т-181 работа ({ThreadSemester(threadIndex)},{cycle})",
                            AssignmentUrl = null,
                            DefenseRequired = false,
                            CreatedAt = now,
                            UpdatedAt = now,
                        };
                        labs.Add(lab);
                        ownLabs[cycle] = lab.Id;
                        labIds.Add(lab.Id);
                        Assert.NotNull(labs.TryGetByPair(ThreadSemester(threadIndex), cycle));
                        break;
                    }

                    case 3:
                    {
                        // Создание сдачи собственной пары (student k, lab k).
                        var pair = (StudentId: ownStudents[cycle], LabId: ownLabs[cycle]);
                        var saved = submissions.Upsert(
                            pair.StudentId,
                            pair.LabId,
                            new DateOnly(2026, 9, 1),
                            null,
                            null,
                            now);
                        Assert.NotNull(saved);
                        submissionPairs.Add(pair);
                        Assert.NotNull(submissions.GetByStudentAndLab(pair.StudentId, pair.LabId));
                        break;
                    }

                    case 4:
                    {
                        // Контрольные чтения всех четырёх созданных сущностей цикла.
                        Assert.NotNull(users.GetById(ownStudents[cycle]));
                        Assert.NotNull(groups.GetById(ownGroups[cycle]));
                        Assert.NotNull(labs.GetById(ownLabs[cycle]));
                        Assert.NotNull(submissions.GetByStudentAndLab(ownStudents[cycle], ownLabs[cycle]));
                        break;
                    }

                    case 5:
                    {
                        // Обновление работы: контент и UpdatedAt.
                        var lab = labs.GetById(ownLabs[cycle]);
                        Assert.NotNull(lab);
                        lab!.Content += $" (обновлена потоком {threadIndex})";
                        labs.Update(lab);
                        var reloaded = labs.TryGetByPair(ThreadSemester(threadIndex), cycle);
                        Assert.NotNull(reloaded);
                        Assert.Contains($"(обновлена потоком {threadIndex})", reloaded!.Content, StringComparison.Ordinal);
                        break;
                    }

                    case 6:
                    {
                        // Обновление группы: переименование в пределах своего
                        // непересекающегося диапазона имён.
                        var group = groups.GetById(ownGroups[cycle]);
                        Assert.NotNull(group);
                        group!.Name += "-ren";
                        groups.Update(group);
                        Assert.NotNull(groups.GetByName(group.Name));
                        break;
                    }

                    case 7:
                    {
                        // Обновление студента: узкая атомарная мутация группы
                        // (SetGroup, SEC-001) на собственную группу цикла.
                        var result = users.SetGroup(
                            ownStudents[cycle],
                            ownGroups[cycle],
                            groupId => groups.GetById(groupId) is not null);
                        Assert.Equal(SetGroupResult.Success, result);
                        Assert.Equal(ownGroups[cycle], users.GetById(ownStudents[cycle])!.GroupId);
                        break;
                    }

                    case 8:
                    {
                        // Обновление сдачи: апсерт с новыми датами (запись пары
                        // сохраняется, даты заменяются).
                        var saved = submissions.Upsert(
                            ownStudents[cycle],
                            ownLabs[cycle],
                            new DateOnly(2026, 9, 15),
                            new DateOnly(2026, 9, 20),
                            null,
                            now);
                        Assert.NotNull(saved);
                        var reloaded = submissions.GetByStudentAndLab(ownStudents[cycle], ownLabs[cycle]);
                        Assert.Equal(new DateOnly(2026, 9, 15), reloaded!.SubmitDate);
                        Assert.Equal(new DateOnly(2026, 9, 20), reloaded.DefenseDate);
                        break;
                    }

                    default:
                    {
                        // Смешанные чтения списков (шаг 9).
                        Assert.Contains(
                            labs.ListByFilter(ThreadSemester(threadIndex)),
                            lab => lab.Id == ownLabs[cycle]);
                        Assert.NotEmpty(users.ListStudents());
                        Assert.NotEmpty(groups.GetAll());
                        break;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            failures.Add($"поток {threadIndex}: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
