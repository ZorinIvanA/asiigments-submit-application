using LabsApp.Domain.Entities;
using LabsApp.Storage.InMemory;

namespace LabsApp.Tests.Storage;

/// <summary>
/// Юнит-проверки InMemorySubmissionRepository (FR-024/IF-015/FR-021): upsert
/// (а) создание, (б) полная замена обеих дат, (в) сброс обеих дат без удаления
/// записи (data_design), выборки (GetByPairs, ListForStudentAndSemester) и
/// потокобезопасность.
/// </summary>
public sealed class InMemorySubmissionRepositoryTests
{
    private readonly Guid _studentId = Guid.NewGuid();
    private readonly Guid _labId = Guid.NewGuid();
    private readonly Guid _updaterId = Guid.NewGuid();

    [Fact]
    public void Upsert_BothDatesSet_Creates()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        var updatedAt = DateTime.UtcNow;

        var created = submissions.Upsert(
            _studentId, _labId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 11), _updaterId, updatedAt);

        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(new DateOnly(2026, 9, 1), created.SubmitDate);
        Assert.Equal(new DateOnly(2026, 9, 11), created.DefenseDate);
        Assert.Equal(_updaterId, created.UpdatedBy);
        Assert.Equal(updatedAt, created.UpdatedAt);
        Assert.Equal(created.Id, submissions.GetByStudentAndLab(_studentId, _labId)!.Id);
    }

    [Fact]
    public void Upsert_DefenseOnly_Creates_RecordWithoutSubmitDate()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());

        var created = submissions.Upsert(_studentId, _labId, null, new DateOnly(2026, 9, 11), _updaterId, DateTime.UtcNow);

        Assert.NotNull(created);
        Assert.Null(created.SubmitDate);
        Assert.Equal(new DateOnly(2026, 9, 11), created.DefenseDate);
    }

    [Fact]
    public void Upsert_Existing_FullyReplacesBothDates_KeepsId()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        var created = submissions.Upsert(
            _studentId, _labId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 11), _updaterId, DateTime.UtcNow);
        var secondUpdater = Guid.NewGuid();
        var secondUpdateAt = DateTime.UtcNow.AddMinutes(1);

        var replaced = submissions.Upsert(_studentId, _labId, new DateOnly(2026, 9, 2), null, secondUpdater, secondUpdateAt);

        Assert.NotNull(replaced);
        Assert.Equal(created!.Id, replaced.Id); // та же логическая запись
        Assert.Equal(new DateOnly(2026, 9, 2), replaced.SubmitDate); // полная замена
        Assert.Null(replaced.DefenseDate); // в т.ч. сброс отсутствующим значением
        Assert.Equal(secondUpdater, replaced.UpdatedBy);
        Assert.Equal(secondUpdateAt, replaced.UpdatedAt);
    }

    [Fact]
    public void Upsert_BothDatesNull_KeepsRecord_ResetsDates_StableId()
    {
        // data_design (FR-021): обе даты null = сброс дат, запись пары сохраняется;
        // Id стабилен, метки UpdatedBy/UpdatedAt заменяются переданными.
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        var createdAt = DateTime.UtcNow;
        var created = submissions.Upsert(
            _studentId, _labId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 11), _updaterId, createdAt);
        var resetUpdater = Guid.NewGuid();
        var resetAt = createdAt.AddMinutes(1);

        var reset = submissions.Upsert(_studentId, _labId, null, null, resetUpdater, resetAt);

        Assert.NotNull(created);
        Assert.NotNull(reset);
        Assert.Equal(created!.Id, reset!.Id); // та же логическая запись
        Assert.Null(reset.SubmitDate); // сброс
        Assert.Null(reset.DefenseDate); // сброс
        Assert.Equal(resetUpdater, reset.UpdatedBy);
        Assert.Equal(resetAt, reset.UpdatedAt);

        var stored = submissions.GetByStudentAndLab(_studentId, _labId);
        Assert.NotNull(stored);
        Assert.Equal(created.Id, stored!.Id);
        Assert.Null(stored.SubmitDate);
        Assert.Null(stored.DefenseDate);
        Assert.Single(submissions.ListByStudent(_studentId));
    }

    [Fact]
    public void Upsert_BothDatesNull_OnEmptyStore_CreatesRecordWithNullDates()
    {
        // data_design (FR-021): записи с обеими null-датами сохраняются —
        // upsert пустой пары создаёт запись с датами null.
        var submissions = new InMemorySubmissionRepository(new StorageLock());

        var result = submissions.Upsert(_studentId, _labId, null, null, null, DateTime.UtcNow);

        Assert.NotNull(result);
        Assert.Null(result!.SubmitDate);
        Assert.Null(result.DefenseDate);
        var stored = submissions.GetByStudentAndLab(_studentId, _labId);
        Assert.NotNull(stored);
        Assert.Equal(result.Id, stored!.Id);
        Assert.Single(submissions.ListByStudent(_studentId));
    }

    [Fact]
    public void GetByStudentAndLab_Miss_ReturnsNull()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());

        Assert.Null(submissions.GetByStudentAndLab(_studentId, _labId));
    }

    [Fact]
    public void ListByLabIds_FiltersAcrossStudents()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        var otherLab = Guid.NewGuid();
        submissions.Upsert(Guid.NewGuid(), _labId, new DateOnly(2026, 9, 1), null, null, DateTime.UtcNow);
        submissions.Upsert(Guid.NewGuid(), _labId, new DateOnly(2026, 9, 2), null, null, DateTime.UtcNow);
        submissions.Upsert(Guid.NewGuid(), otherLab, new DateOnly(2026, 9, 3), null, null, DateTime.UtcNow);

        var result = submissions.ListByLabIds([_labId]);

        Assert.Equal(2, result.Count);
        Assert.All(result, submission => Assert.Equal(_labId, submission.LabId));
    }

    [Fact]
    public void ListByLabIds_EmptyCollection_ReturnsEmpty()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        submissions.Upsert(Guid.NewGuid(), _labId, new DateOnly(2026, 9, 1), null, null, DateTime.UtcNow);

        Assert.Empty(submissions.ListByLabIds([]));
    }

    [Fact]
    public void ListByStudent_FiltersByStudent()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        var otherStudent = Guid.NewGuid();
        submissions.Upsert(_studentId, Guid.NewGuid(), new DateOnly(2026, 9, 1), null, null, DateTime.UtcNow);
        submissions.Upsert(_studentId, Guid.NewGuid(), new DateOnly(2026, 9, 2), null, null, DateTime.UtcNow);
        submissions.Upsert(otherStudent, _labId, new DateOnly(2026, 9, 3), null, null, DateTime.UtcNow);

        Assert.Equal(2, submissions.ListByStudent(_studentId).Count);
        Assert.Single(submissions.ListByStudent(otherStudent));
    }

    [Fact]
    public void DeleteByLabId_RemovesOnlyTargetLabSubmissions()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        var otherLab = Guid.NewGuid();
        submissions.Upsert(_studentId, _labId, new DateOnly(2026, 9, 1), null, null, DateTime.UtcNow);
        submissions.Upsert(_studentId, otherLab, new DateOnly(2026, 9, 2), null, null, DateTime.UtcNow);

        submissions.DeleteByLabId(_labId);

        Assert.Null(submissions.GetByStudentAndLab(_studentId, _labId));
        Assert.NotNull(submissions.GetByStudentAndLab(_studentId, otherLab));
    }

    [Fact]
    public async Task Upsert_ConcurrentSamePair_SingleConsistentRecord()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());

        // Эталонный набор атомарных вариантов (submit, defense, updater) — каждая
        // попытка пишет согласованную тройку; финальная запись обязана совпасть
        // с ОДНИМ из эталонов целиком (смесь полей разных попыток — дефект).
        var updaters = new Guid?[] { _updaterId, Guid.NewGuid(), Guid.NewGuid() };
        var referenceTuples = Enumerable.Range(0, 100)
            .Select(i => (
                Submit: (DateOnly?)new DateOnly(2026, 1, 1).AddDays(i),
                Defense: (DateOnly?)(i % 2 == 0 ? new DateOnly(2026, 2, 1).AddDays(i) : null),
                Updater: updaters[i % updaters.Length]))
            .ToHashSet();

        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() => submissions.Upsert(
                _studentId,
                _labId,
                new DateOnly(2026, 1, 1).AddDays(i),
                i % 2 == 0 ? new DateOnly(2026, 2, 1).AddDays(i) : null,
                updaters[i % updaters.Length],
                DateTime.UtcNow)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Ровно одна запись пары; все попытки отдали один и тот же Id.
        var stored = submissions.GetByStudentAndLab(_studentId, _labId);
        Assert.NotNull(stored);
        Assert.Single(submissions.ListByStudent(_studentId));
        Assert.All(results, result => Assert.Equal(stored!.Id, result!.Id));

        // Запись согласована: тройка полей целиком из одного атомарного варианта.
        Assert.Contains(
            (stored.SubmitDate, stored.DefenseDate, stored.UpdatedBy),
            referenceTuples);
    }

    // ------------------------------------------------------------------
    // Запросные расширения T-104 (IF-015, FR-021): GetByPairs и
    // ListForStudentAndSemester.
    // ------------------------------------------------------------------

    [Fact]
    public void GetByPairs_ReturnsOnlyRequestedPairs()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        var student1 = Guid.NewGuid();
        var student2 = Guid.NewGuid();
        var student3 = Guid.NewGuid();
        var lab1 = Guid.NewGuid();
        var lab2 = Guid.NewGuid();
        var lab3 = Guid.NewGuid();
        submissions.Upsert(student1, lab1, new DateOnly(2026, 9, 1), null, null, DateTime.UtcNow);
        submissions.Upsert(student2, lab1, new DateOnly(2026, 9, 2), null, null, DateTime.UtcNow);
        submissions.Upsert(student1, lab2, new DateOnly(2026, 9, 3), null, null, DateTime.UtcNow);
        submissions.Upsert(student3, lab3, new DateOnly(2026, 9, 4), null, null, DateTime.UtcNow); // вне запроса

        var found = submissions.GetByPairs([student1, student2], [lab1, lab2]);

        Assert.Equal(3, found.Count);
        Assert.All(found, submission =>
            Assert.Contains((submission.StudentId, submission.LabId),
                new[] { (student1, lab1), (student2, lab1), (student1, lab2) }));
        Assert.DoesNotContain(found, submission => submission.StudentId == student3);
        Assert.DoesNotContain(found, submission => submission.LabId == lab3);
    }

    [Fact]
    public void GetByPairs_MissingPairs_ReturnsOnlyExisting()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        submissions.Upsert(_studentId, _labId, new DateOnly(2026, 9, 1), null, null, DateTime.UtcNow);

        var found = submissions.GetByPairs([_studentId, Guid.NewGuid()], [_labId, Guid.NewGuid()]);

        Assert.Single(found);
        Assert.Equal(_studentId, found[0].StudentId);
        Assert.Equal(_labId, found[0].LabId);
    }

    [Fact]
    public void GetByPairs_EmptyInput_ReturnsEmpty()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        submissions.Upsert(_studentId, _labId, new DateOnly(2026, 9, 1), null, null, DateTime.UtcNow);

        Assert.Empty(submissions.GetByPairs([], [_labId]));
        Assert.Empty(submissions.GetByPairs([_studentId], []));
        Assert.Empty(submissions.GetByPairs([], []));
    }

    [Fact]
    public void GetByPairs_DuplicateInput_NoDuplicates()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        submissions.Upsert(_studentId, _labId, new DateOnly(2026, 9, 1), null, null, DateTime.UtcNow);

        var found = submissions.GetByPairs([_studentId, _studentId], [_labId, _labId]);

        Assert.Single(found);
    }

    [Fact]
    public void ListForStudentAndSemester_ProjectsStudentSubmissionsOntoSemesterLabs()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        var otherStudent = Guid.NewGuid();
        var sem1Lab1 = Guid.NewGuid();
        var sem1Lab2 = Guid.NewGuid();
        var sem2Lab = Guid.NewGuid();
        submissions.Upsert(_studentId, sem1Lab1, new DateOnly(2026, 9, 1), null, null, DateTime.UtcNow);
        submissions.Upsert(_studentId, sem1Lab2, new DateOnly(2026, 9, 2), null, null, DateTime.UtcNow);
        submissions.Upsert(_studentId, sem2Lab, new DateOnly(2026, 10, 1), null, null, DateTime.UtcNow); // другой семестр
        submissions.Upsert(otherStudent, sem1Lab1, new DateOnly(2026, 9, 3), null, null, DateTime.UtcNow); // чужая

        var found = submissions.ListForStudentAndSemester(_studentId, [sem1Lab1, sem1Lab2]);

        Assert.Equal(2, found.Count);
        Assert.All(found, submission =>
        {
            Assert.Equal(_studentId, submission.StudentId);
            Assert.NotEqual(sem2Lab, submission.LabId);
        });
    }

    [Fact]
    public void ListForStudentAndSemester_EmptyLabSet_ReturnsEmpty()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        submissions.Upsert(_studentId, _labId, new DateOnly(2026, 9, 1), null, null, DateTime.UtcNow);

        Assert.Empty(submissions.ListForStudentAndSemester(_studentId, []));
    }

    [Fact]
    public void ListForStudentAndSemester_UnknownStudent_ReturnsEmpty()
    {
        var submissions = new InMemorySubmissionRepository(new StorageLock());
        submissions.Upsert(_studentId, _labId, new DateOnly(2026, 9, 1), null, null, DateTime.UtcNow);

        Assert.Empty(submissions.ListForStudentAndSemester(Guid.NewGuid(), [_labId]));
    }
}
