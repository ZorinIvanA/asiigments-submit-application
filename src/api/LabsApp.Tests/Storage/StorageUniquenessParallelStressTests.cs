using LabsApp.Domain;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;

namespace LabsApp.Tests.Storage;

/// <summary>
/// Параллельный стресс каждой уникальности IF-015 (FR-024, T-104
/// unit_test_requirements): конкурентные создания, специально сталкивающиеся
/// на одном ci-ключе/паре (lower(login), lower(email), lower(name),
/// (semester,number), (studentId,labId)), — ровно один успех, остальные —
/// ожидаемый <see cref="StorageConflictException"/> (дублей и «неожиданных»
/// исключений нет). Стартовые барьеры гарантируют реальное столкновение.
/// </summary>
public sealed class StorageUniquenessParallelStressTests
{
    private const int Contenders = 32;

    [Fact]
    public async Task ParallelCreate_SameLoginCi_ExactlyOneWinner()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        using var startGate = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, Contenders)
            .Select(contenderIndex => Task.Run(() =>
            {
                startGate.Wait();
                try
                {
                    users.Add(TestEntities.User(
                        $"same-LOGIN",
                        $"unique-email-{contenderIndex}@example.com"));
                    return true;
                }
                catch (StorageConflictException)
                {
                    return false;
                }
            }))
            .ToArray();

        startGate.Set();
        await Task.WhenAll(tasks);

        Assert.Equal(1, tasks.Count(task => task.Result));
        Assert.Single(users.ListStudents());
        Assert.NotNull(users.GetByLogin("SAME-login"));
    }

    [Fact]
    public async Task ParallelCreate_SameEmailCi_ExactlyOneWinner()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        using var startGate = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, Contenders)
            .Select(contenderIndex => Task.Run(() =>
            {
                startGate.Wait();
                try
                {
                    users.Add(TestEntities.User(
                        $"unique-login-{contenderIndex}",
                        $"Same@Example.COM"));
                    return true;
                }
                catch (StorageConflictException)
                {
                    return false;
                }
            }))
            .ToArray();

        startGate.Set();
        await Task.WhenAll(tasks);

        Assert.Equal(1, tasks.Count(task => task.Result));
        Assert.Single(users.ListStudents());
        Assert.NotNull(users.GetByEmail("same@example.com"));
    }

    [Fact]
    public async Task ParallelCreate_SameGroupNameCi_ExactlyOneWinner()
    {
        var storage = TestStorage.Create();
        using var startGate = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, Contenders)
            .Select(_ => Task.Run(() =>
            {
                startGate.Wait();
                try
                {
                    storage.Groups.Add(TestEntities.Group("ИК-221"));
                    return true;
                }
                catch (StorageConflictException)
                {
                    return false;
                }
            }))
            .ToArray();

        startGate.Set();
        await Task.WhenAll(tasks);

        Assert.Equal(1, tasks.Count(task => task.Result));
        Assert.Single(storage.Groups.List());
        Assert.NotNull(storage.Groups.GetByName("ик-221"));
        Assert.True(storage.Groups.ExistsNameCi("ИК-221"));
    }

    /// <summary>
    /// AC T-104/FR-024 «Атомарность уникальности»: два и более параллельных
    /// Create с одной парой (semester, number) — ровно один успех, остальные —
    /// конфликт; дублей нет.
    /// </summary>
    [Fact]
    public async Task ParallelCreate_SameLabPair_ExactlyOneWinner_NoDuplicates()
    {
        var storage = TestStorage.Create();
        using var startGate = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, Contenders)
            .Select(_ => Task.Run(() =>
            {
                startGate.Wait();
                try
                {
                    storage.Labs.Add(TestEntities.Lab(1, 1));
                    return true;
                }
                catch (StorageConflictException)
                {
                    return false;
                }
            }))
            .ToArray();

        startGate.Set();
        await Task.WhenAll(tasks);

        Assert.Equal(1, tasks.Count(task => task.Result));
        var winner = storage.Labs.TryGetByPair(1, 1);
        Assert.NotNull(winner);
        Assert.Single(storage.Labs.GetAll());
        Assert.True(storage.Labs.ExistsPair(1, 1));
        Assert.False(storage.Labs.ExistsPair(1, 1, winner.Id));
        Assert.Equal(1, storage.Labs.Semesters().Single());
    }

    /// <summary>
    /// Уникальность (studentId, labId) при конкурентном upsert: сколько бы
    /// потоков ни писали пару, хранилище содержит ровно одну запись, индекс
    /// пары и обратное разрешение согласованы, а тройка полей записи совпадает
    /// с одним из атомарных вариантов попыток (без смеси полей).
    /// </summary>
    [Fact]
    public async Task ParallelUpsert_SameSubmissionPair_SingleRecord_IndexConsistent()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        var studentId = Guid.NewGuid();
        var labId = Guid.NewGuid();
        var updaterId = Guid.NewGuid();
        using var startGate = new ManualResetEventSlim();

        var referenceTuples = Enumerable.Range(0, Contenders)
            .Select(i => (
                Submit: (DateOnly?)new DateOnly(2026, 9, 1).AddDays(i),
                Defense: (DateOnly?)(i % 2 == 0 ? new DateOnly(2026, 10, 1).AddDays(i) : null),
                Updater: (Guid?)updaterId))
            .ToHashSet();

        var tasks = Enumerable.Range(0, Contenders)
            .Select(i => Task.Run(() =>
            {
                startGate.Wait();
                return submissions.Upsert(
                    studentId,
                    labId,
                    new DateOnly(2026, 9, 1).AddDays(i),
                    i % 2 == 0 ? new DateOnly(2026, 10, 1).AddDays(i) : null,
                    updaterId,
                    DateTime.UtcNow);
            }))
            .ToArray();

        startGate.Set();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, Assert.NotNull);
        Assert.Single(submissions.ListByStudent(studentId));
        var stored = submissions.GetByStudentAndLab(studentId, labId)!;
        Assert.All(results, result => Assert.Equal(stored.Id, result!.Id));
        Assert.Contains((stored.SubmitDate, stored.DefenseDate, stored.UpdatedBy), referenceTuples);
    }

    /// <summary>
    /// Конкурентная смесь upsert-созданий и сбросов (обе даты null = сброс дат,
    /// data_design) одной пары: исключений нет; ровно одна запись пары, её даты
    /// совпадают с одним из атомарных вариантов попыток.
    /// </summary>
    [Fact]
    public async Task ParallelUpsert_MixedDatesAndResets_ConsistentFinalState()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        var studentId = Guid.NewGuid();
        var labId = Guid.NewGuid();
        using var startGate = new ManualResetEventSlim();

        var referenceTuples = Enumerable.Range(0, Contenders)
            .Select(i => i % 2 == 1
                ? (Submit: (DateOnly?)null, Defense: (DateOnly?)null)
                : (Submit: (DateOnly?)new DateOnly(2026, 9, 1).AddDays(i), Defense: (DateOnly?)null))
            .ToHashSet();

        var tasks = Enumerable.Range(0, Contenders)
            .Select(i => Task.Run(() =>
            {
                startGate.Wait();
                var reset = i % 2 == 1;
                return submissions.Upsert(
                    studentId,
                    labId,
                    reset ? null : new DateOnly(2026, 9, 1).AddDays(i),
                    null,
                    null,
                    DateTime.UtcNow);
            }))
            .ToArray();

        startGate.Set();
        await Task.WhenAll(tasks);

        var remaining = submissions.ListByStudent(studentId);
        var stored = submissions.GetByStudentAndLab(studentId, labId);
        Assert.NotNull(stored);
        Assert.Single(remaining);
        Assert.Equal(stored!.Id, remaining[0].Id);
        Assert.Contains((stored.SubmitDate, stored.DefenseDate), referenceTuples);
    }
}
