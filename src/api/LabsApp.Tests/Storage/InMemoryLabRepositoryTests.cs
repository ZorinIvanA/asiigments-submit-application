using LabsApp.Domain.Entities;
using LabsApp.Storage;

namespace LabsApp.Tests.Storage;

/// <summary>
/// Юнит-проверки InMemoryLabRepository (FR-002/IF-015): уникальность пары
/// (семестр, номер), TryGetByPair, Update с простановкой UpdatedAt, каскад
/// удаления сдач (AC FR-002 «Каскады») и запросные расширения T-104
/// (ListByFilter, Semesters, ExistsPair).
/// </summary>
public sealed class InMemoryLabRepositoryTests
{
    [Fact]
    public void Add_AndTryGetByPair()
    {
        var storage = TestStorage.Create();
        var lab = TestEntities.Lab(1, 5);
        storage.Labs.Add(lab);

        Assert.Equal(lab.Id, storage.Labs.GetById(lab.Id)!.Id);
        Assert.Equal(lab.Id, storage.Labs.TryGetByPair(1, 5)!.Id);
        Assert.Null(storage.Labs.TryGetByPair(1, 6));
        Assert.Null(storage.Labs.TryGetByPair(2, 5));
    }

    [Fact]
    public void Add_DuplicatePair_Conflict_OtherSemesterOk()
    {
        var storage = TestStorage.Create();
        storage.Labs.Add(TestEntities.Lab(1, 5));

        Assert.Throws<StorageConflictException>(
            () => storage.Labs.Add(TestEntities.Lab(1, 5)));

        storage.Labs.Add(TestEntities.Lab(2, 5));
        Assert.NotNull(storage.Labs.TryGetByPair(2, 5));
    }

    [Fact]
    public void GetAll_ReturnsAll()
    {
        var storage = TestStorage.Create();
        storage.Labs.Add(TestEntities.Lab(1, 1));
        storage.Labs.Add(TestEntities.Lab(1, 2));
        storage.Labs.Add(TestEntities.Lab(2, 1));

        Assert.Equal(3, storage.Labs.GetAll().Count);
    }

    [Fact]
    public void Update_SetsUpdatedAt_FromTimeProvider()
    {
        // ADR-002/FR-003 (CR-001): единственный источник бизнес-времени — TimeProvider;
        // Update проставляет UpdatedAt по FakeTimeProvider — проверка детерминирована.
        var storage = TestStorage.Create();
        var createdAt = storage.Time.GetUtcNow().UtcDateTime;
        var lab = TestEntities.Lab(1, 1);
        lab.UpdatedAt = createdAt;
        storage.Labs.Add(lab);

        var updateMoment = createdAt.AddHours(1);
        storage.Time.SetUtcNow(updateMoment);
        lab.Content = "Новое содержание";
        storage.Labs.Update(lab);

        var stored = storage.Labs.GetById(lab.Id)!;
        Assert.Equal("Новое содержание", stored.Content);
        Assert.Equal(updateMoment, stored.UpdatedAt);
    }

    [Fact]
    public void Update_SamePair_Ok_OtherPair_Conflict()
    {
        var storage = TestStorage.Create();
        var first = TestEntities.Lab(1, 1);
        var second = TestEntities.Lab(1, 2);
        storage.Labs.Add(first);
        storage.Labs.Add(second);

        // Та же пара у самой записи — владелец совпадает, конфликт нет.
        first.Content = "Правка без смены пары";
        storage.Labs.Update(first);

        // Перенос на занятую пару другой записи — конфликт, пара не меняется.
        first.Number = 2;
        Assert.Throws<StorageConflictException>(() => storage.Labs.Update(first));
        Assert.NotNull(storage.Labs.TryGetByPair(1, 1));
        Assert.Equal(second.Id, storage.Labs.TryGetByPair(1, 2)!.Id);
    }

    [Fact]
    public void Update_Missing_Throws()
    {
        var storage = TestStorage.Create();

        Assert.Throws<InvalidOperationException>(
            () => storage.Labs.Update(TestEntities.Lab(1, 1)));
    }

    // ------------------------------------------------------------------
    // Каскад (AC FR-002 «Каскады»): удаление работы — её сдачи удалены.
    // ------------------------------------------------------------------

    [Fact]
    public void Delete_SubmissionsOfLab_Removed_OthersKept()
    {
        var storage = TestStorage.Create();
        var removedLab = TestEntities.Lab(1, 1);
        var keptLab = TestEntities.Lab(1, 2);
        storage.Labs.Add(removedLab);
        storage.Labs.Add(keptLab);

        var student = TestEntities.User("s1");
        storage.Users.Add(student);

        var removedSubmission = storage.Submissions.Upsert(
            student.Id, removedLab.Id, new DateOnly(2026, 9, 1), null, null, DateTime.UtcNow);
        var otherSubmission = storage.Submissions.Upsert(
            student.Id, keptLab.Id, new DateOnly(2026, 9, 2), null, null, DateTime.UtcNow);

        Assert.NotNull(removedSubmission);
        Assert.NotNull(otherSubmission);

        storage.Labs.Delete(removedLab.Id);

        Assert.Null(storage.Labs.GetById(removedLab.Id));
        Assert.Null(storage.Labs.TryGetByPair(1, 1));
        Assert.Null(storage.Submissions.GetByStudentAndLab(student.Id, removedLab.Id));
        Assert.NotNull(storage.Submissions.GetByStudentAndLab(student.Id, keptLab.Id));
    }

    [Fact]
    public void Delete_Missing_NoOp()
    {
        var storage = TestStorage.Create();
        storage.Labs.Add(TestEntities.Lab(1, 1));

        storage.Labs.Delete(Guid.NewGuid());

        Assert.Single(storage.Labs.GetAll());
    }

    // ------------------------------------------------------------------
    // Запросные расширения T-104 (IF-015, FR-017/FR-018): ListByFilter,
    // Semesters, ExistsPair.
    // ------------------------------------------------------------------

    [Fact]
    public void ListByFilter_Null_ReturnsAllLabs()
    {
        var storage = TestStorage.Create();
        storage.Labs.Add(TestEntities.Lab(1, 1));
        storage.Labs.Add(TestEntities.Lab(1, 2));
        storage.Labs.Add(TestEntities.Lab(2, 1));

        Assert.Equal(3, storage.Labs.ListByFilter(null).Count);
    }

    [Fact]
    public void ListByFilter_Semester_ExactEquality_OtherSemestersExcluded()
    {
        var storage = TestStorage.Create();
        var sem1Lab1 = TestEntities.Lab(1, 1);
        var sem1Lab2 = TestEntities.Lab(1, 2);
        var sem2Lab1 = TestEntities.Lab(2, 1);
        storage.Labs.Add(sem1Lab1);
        storage.Labs.Add(sem1Lab2);
        storage.Labs.Add(sem2Lab1);

        var sem1 = storage.Labs.ListByFilter(1);
        var sem2 = storage.Labs.ListByFilter(2);

        Assert.Equal(2, sem1.Count);
        Assert.All(sem1, lab => Assert.Equal(1, lab.Semester));
        Assert.Contains(sem1, lab => lab.Id == sem1Lab1.Id);
        Assert.Contains(sem1, lab => lab.Id == sem1Lab2.Id);
        Assert.Single(sem2);
        Assert.Equal(sem2Lab1.Id, sem2[0].Id);
    }

    [Fact]
    public void ListByFilter_UnknownSemester_ReturnsEmpty()
    {
        var storage = TestStorage.Create();
        storage.Labs.Add(TestEntities.Lab(1, 1));

        // Внедиапазонное целое фильтра — пустая выборка, а не ошибка (ASM-018:
        // мягкая семантика; нормализация параметра — зона контроллера).
        Assert.Empty(storage.Labs.ListByFilter(99));
    }

    [Fact]
    public void ListByFilter_AfterDelete_ReflectsState()
    {
        var storage = TestStorage.Create();
        var lab = TestEntities.Lab(1, 1);
        storage.Labs.Add(lab);
        storage.Labs.Add(TestEntities.Lab(2, 1));

        storage.Labs.Delete(lab.Id);

        Assert.Empty(storage.Labs.ListByFilter(1));
        Assert.Single(storage.Labs.ListByFilter(2));
        // Семестр 1 исчез вместе с последней работой, остался только семестр 2.
        Assert.Equal(2, storage.Labs.Semesters().Single());
    }

    [Fact]
    public void Semesters_DistinctAscending()
    {
        var storage = TestStorage.Create();
        storage.Labs.Add(TestEntities.Lab(2, 1));
        storage.Labs.Add(TestEntities.Lab(1, 5));
        storage.Labs.Add(TestEntities.Lab(2, 9));
        storage.Labs.Add(TestEntities.Lab(3, 1));

        Assert.Equal(new[] { 1, 2, 3 }, storage.Labs.Semesters());
    }

    [Fact]
    public void Semesters_EmptyStorage_ReturnsEmpty()
    {
        var storage = TestStorage.Create();

        Assert.Empty(storage.Labs.Semesters());
    }

    [Fact]
    public void ExistsPair_MissingPair_False_ExistingPair_True()
    {
        var storage = TestStorage.Create();
        var lab = TestEntities.Lab(1, 1);
        storage.Labs.Add(lab);

        Assert.True(storage.Labs.ExistsPair(1, 1));
        Assert.False(storage.Labs.ExistsPair(1, 2));
        Assert.False(storage.Labs.ExistsPair(2, 1));
        Assert.False(storage.Labs.ExistsPair(1, 1, lab.Id));
    }

    [Fact]
    public void ExistsPair_ExceptId_OwnPairNotConflict_OtherPairIs()
    {
        // Зеркало семантики Update: собственная пара (exceptId) конфликтом не считается.
        var storage = TestStorage.Create();
        var own = TestEntities.Lab(1, 1);
        var other = TestEntities.Lab(1, 2);
        storage.Labs.Add(own);
        storage.Labs.Add(other);

        Assert.False(storage.Labs.ExistsPair(1, 1, own.Id));
        Assert.True(storage.Labs.ExistsPair(1, 2, own.Id));
        Assert.True(storage.Labs.ExistsPair(1, 2, Guid.NewGuid()));
    }

    [Fact]
    public void ExistsPair_AfterUpdateAndDelete_TracksIndex()
    {
        var storage = TestStorage.Create();
        var lab = TestEntities.Lab(1, 1);
        storage.Labs.Add(lab);

        // Перенос пары: старая свободна, новая занята.
        lab.Semester = 2;
        lab.Number = 3;
        storage.Labs.Update(lab);

        Assert.False(storage.Labs.ExistsPair(1, 1));
        Assert.True(storage.Labs.ExistsPair(2, 3));

        storage.Labs.Delete(lab.Id);

        Assert.False(storage.Labs.ExistsPair(2, 3));
    }
}
