using System.Collections.Concurrent;
using LabsApp.Domain;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;

namespace LabsApp.Tests.Storage;

/// <summary>
/// Конкурентный гейт NFR-005 (T-104): 8 потоков × 200 смешанных операций
/// (создание/чтение/обновление labs, groups, students, submissions) над
/// заполненными in-memory репозиториями под общей <see cref="StorageLock"/>.
/// Гейт: ни одного неожиданного исключения; инварианты уникальности
/// (semester,number), (studentId,labId) и ci-ключи login/email/названия групп
/// не нарушены. Конкурентные вставки специально сталкиваются на общих ключах:
/// ожидаемый <see cref="StorageConflictException"/> — нормальный исход (ровно
/// один победитель), любой другой исключение проваливает гейт.
/// </summary>
public sealed class StorageConcurrencyGateTests
{
    private const int Threads = 8;
    private const int OperationsPerThread = 200;
    private const int SeededStudentsPerGroup = 10;
    private const int SeededSemesterTwoLabs = 5;

    [Fact]
    public async Task EightThreads_Times200MixedOperations_NoExceptions_UniqueInvariantsHold()
    {
        var storage = TestStorage.Create();

        // --- Предзаполнение: группы, студенты, преподаватель, работы, сдачи. ---
        var groupA = TestEntities.Group("ИК-221");
        var groupB = TestEntities.Group("ИК-222");
        await storage.Groups.AddAsync(groupA);
        await storage.Groups.AddAsync(groupB);

        var teacher = TestEntities.User("teacher", role: UserRoles.Teacher);
        await storage.Users.AddAsync(teacher);

        var seededStudents = new List<User>();
        for (var n = 0; n < SeededStudentsPerGroup * 2; n++)
        {
            var student = TestEntities.User(
                $"seeded{n:00}",
                groupId: n % 2 == 0 ? groupA.Id : groupB.Id,
                fullName: $"Сидов Сид Сидович {n:00}");
            await storage.Users.AddAsync(student);
            seededStudents.Add(student);
        }

        // Работы сид-семестра (номера 101+, чтобы не пересекаться с гонкой (1, i)).
        var seededSemesterLabIds = new List<Guid>();
        for (var number = 101; number < 101 + SeededSemesterTwoLabs; number++)
        {
            var lab = TestEntities.Lab(2, number);
            await storage.Labs.AddAsync(lab);
            seededSemesterLabIds.Add(lab.Id);
        }

        // Сид-сдачи для смешанных чтений ведомости.
        await storage.Submissions.UpsertAsync(
            seededStudents[0].Id, seededSemesterLabIds[0], new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10), teacher.Id, DateTime.UtcNow);
        await storage.Submissions.UpsertAsync(
            seededStudents[1].Id, seededSemesterLabIds[1], new DateOnly(2026, 9, 2), null, teacher.Id, DateTime.UtcNow);

        var unexpected = new ConcurrentQueue<Exception>();
        var expectedConflicts = 0;
        var seededStudentIds = seededStudents.Select(student => student.Id).ToList();

        // --- Нагрузка: Threads потоков × OperationsPerThread смешанных операций. ---
        using var startGate = new Barrier(Threads);

        var tasks = Enumerable.Range(0, Threads)
            .Select(threadIndex => Task.Run(async () =>
            {
                startGate.SignalAndWait();
                Group? previousGroup = null;

                for (var i = 1; i <= OperationsPerThread; i++)
                {
                    try
                    {
                        // 1. Создание студента с уникальными login/email.
                        var student = TestEntities.User(
                            $"t{threadIndex:D2}-u{i:D4}",
                            $"t{threadIndex:D2}-u{i:D4}@example.com",
                            fullName: $"Студент {threadIndex} {i}");
                        await storage.Users.AddAsync(student);

                        // 2. Гонка за общий ci-ключ login: ровно один из 8 побеждает.
                        try
                        {
                            await storage.Users.AddAsync(TestEntities.User(
                                $"race{i}",
                                $"race{i}@t{threadIndex}.example.com"));
                        }
                        catch (StorageConflictException)
                        {
                            Interlocked.Increment(ref expectedConflicts);
                        }

                        // 3. Гонка за общее ci-имя группы.
                        try
                        {
                            await storage.Groups.AddAsync(TestEntities.Group($"Конкурент-{i}"));
                        }
                        catch (StorageConflictException)
                        {
                            Interlocked.Increment(ref expectedConflicts);
                        }

                        // 4-5. Создание своей группы и обновление предыдущей своей.
                        var ownGroup = TestEntities.Group($"t{threadIndex:D2}-grp{i:D4}");
                        await storage.Groups.AddAsync(ownGroup);

                        if (previousGroup is not null)
                        {
                            previousGroup.Name += "!";
                            await storage.Groups.UpdateAsync(previousGroup);
                        }

                        previousGroup = ownGroup;

                        // 6. Гонка за пару (semester, number) = (1, i).
                        try
                        {
                            await storage.Labs.AddAsync(TestEntities.Lab(1, i));
                        }
                        catch (StorageConflictException)
                        {
                            Interlocked.Increment(ref expectedConflicts);
                        }

                        // 7. Обновление работы семестра 1 (её к этому моменту уже создал
                        //    победитель гонки: собственный Add либо выиграл, либо встретил
                        //    готовую запись) и upsert сдачи по паре.
                        var raceLab = await storage.Labs.TryGetByPairAsync(1, i);
                        if (raceLab is not null)
                        {
                            raceLab.Content += "*";
                            await storage.Labs.UpdateAsync(raceLab);

                            var studentId = seededStudentIds[(i + threadIndex) % seededStudentIds.Count];
                            var submitDate = new DateOnly(2026, 9, 1).AddDays(i % 28);
                            await storage.Submissions.UpsertAsync(
                                studentId, raceLab.Id, submitDate, null, teacher.Id, DateTime.UtcNow);

                            // Периодический сброс: обе даты null = сброс дат; запись
                            // пары сохраняется (data_design, FR-021).
                            if (i % 25 == 0)
                            {
                                await storage.Submissions.UpsertAsync(studentId, raceLab.Id, null, null, null, DateTime.UtcNow);
                            }
                        }

                        // 8. Смешанные чтения (результат не используется — важна
                        //    согласованность и отсутствие исключений).
                        _ = await storage.Users.ListStudentsAsync($"t{threadIndex:D2}-u{i:D4}");
                        _ = await storage.Users.ListStudentsAsync(null, "none");
                        _ = await storage.Users.ListByGroupAsync(groupA.Id);
                        _ = await storage.Users.CountByGroupAsync(groupB.Id);
                        _ = await storage.Submissions.GetByPairsAsync(seededStudentIds, seededSemesterLabIds);
                        _ = await storage.Submissions.ListForStudentAndSemesterAsync(
                            seededStudents[threadIndex % seededStudents.Count].Id, seededSemesterLabIds);

                        if (i % 10 == 0)
                        {
                            _ = await storage.Groups.ListAsync();
                            _ = await storage.Groups.ExistsNameCiAsync("ИК-221");
                            _ = await storage.Labs.GetAllAsync();
                            // Запросные расширения T-104 под той же нагрузкой (NFR-005).
                            _ = await storage.Labs.ListByFilterAsync(1);
                            _ = await storage.Labs.ListByFilterAsync(null);
                            _ = await storage.Labs.SemestersAsync();
                            _ = await storage.Labs.ExistsPairAsync(1, i);
                        }
                    }
                    catch (Exception ex)
                    {
                        unexpected.Enqueue(ex);
                        return;
                    }
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // --- Гейт 1: ни одного неожиданного исключения. ---
        Assert.True(
            unexpected.IsEmpty,
            "Неожиданные исключения под нагрузкой: " + string.Join("; ", unexpected.Select(ex => ex.Message)));

        // Гонки действительно происходили (иначе гейт ничего не измеряет).
        Assert.True(expectedConflicts > 0, "Ожидались конкурентные конфликты уникальности.");

        // --- Гейт 2: исходы гонок — ровно один победитель на общий ключ. ---
        for (var i = 1; i <= OperationsPerThread; i++)
        {
            Assert.NotNull(await await storage.Users.GetByLoginAsync($"race{i}"));
            Assert.NotNull(await await storage.Groups.GetByNameAsync($"Конкурент-{i}"));
            Assert.NotNull(await await storage.Labs.TryGetByPairAsync(1, i));
        }

        // --- Гейт 3: инварианты ci-уникальности пользователей + обратное разрешение. ---
        var students = await storage.Users.ListStudentsAsync();
        Assert.Equal(
            SeededStudentsPerGroup * 2 + (Threads * OperationsPerThread) + OperationsPerThread,
            students.Count); // сид + уникальные + по одному победителю race

        var loginKeys = new HashSet<string>(StringComparer.Ordinal);
        var emailKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var student in students)
        {
            Assert.True(loginKeys.Add(Collation.Key(student.Login)), $"Дубликат ci-login: {student.Login}");
            Assert.True(emailKeys.Add(Collation.Key(student.Email)), $"Дубликат ci-email: {student.Email}");
            Assert.Equal(student.Id, (await storage.Users.GetByLoginAsync(student.Login))!.Id);
            Assert.Equal(student.Id, (await storage.Users.GetByEmailAsync(student.Email))!.Id);
        }

        Assert.Equal(teacher.Id, (await storage.Users.GetByLoginAsync("teacher"))!.Id);

        // --- Гейт 4: инвариант ci-уникальности названий групп. ---
        var groups = await storage.Groups.ListAsync();
        Assert.Equal(2 + OperationsPerThread + (Threads * OperationsPerThread), groups.Count);
        var nameKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            Assert.True(nameKeys.Add(Collation.Key(group.Name)), $"Дубликат ci-имени группы: {group.Name}");
            Assert.Equal(group.Id, (await storage.Groups.GetByNameAsync(group.Name))!.Id);
        }

        // --- Гейт 5: уникальность пар (semester, number) и (studentId, labId). ---
        var allLabs = await storage.Labs.GetAllAsync();
        Assert.Equal(SeededSemesterTwoLabs + OperationsPerThread, allLabs.Count);
        var labPairs = new HashSet<(int Semester, int Number)>();
        foreach (var lab in allLabs)
        {
            Assert.True(labPairs.Add((lab.Semester, lab.Number)), $"Дубликат пары ({lab.Semester},{lab.Number})");
            Assert.Equal(lab.Id, (await storage.Labs.TryGetByPairAsync(lab.Semester, lab.Number))!.Id);
        }

        var allLabIds = allLabs.Select(lab => lab.Id).ToList();
        var submissions = await storage.Submissions.ListByLabIdsAsync(allLabIds);
        var submissionPairs = new HashSet<(Guid StudentId, Guid LabId)>();
        foreach (var submission in submissions)
        {
            Assert.True(
                submissionPairs.Add((submission.StudentId, submission.LabId)),
                $"Дубликат пары сдачи ({submission.StudentId},{submission.LabId})");
            // Обратное разрешение пары стабильно (в т.ч. для записей со сброшенными датами).
            Assert.Equal(
                submission.Id,
                (await storage.Submissions.GetByStudentAndLabAsync(submission.StudentId, submission.LabId))!.Id);
        }
    }
}
