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
    public async Task Add_AndTryGetByPair()
    {
        var storage = TestStorage.Create();
        var lab = TestEntities.Lab(1, 5);
        await storage.Labs.AddAsync(lab);

        Assert.Equal(lab.Id, (await storage.Labs.GetByIdAsync(lab.Id))!.Id);
        Assert.Equal(lab.Id, (await storage.Labs.TryGetByPairAsync(1, 5))!.Id);
        Assert.Null(await await storage.Labs.TryGetByPairAsync(1, 6));
        Assert.Null(await await storage.Labs.TryGetByPairAsync(2, 5));
    }

    [Fact]
    public async Task Add_DuplicatePair_Conflict_OtherSemesterOk()
    {
        var storage = TestStorage.Create();
        await storage.Labs.AddAsync(TestEntities.Lab(1, 5));

        await Assert.ThrowsAsync<StorageConflictException>(
            async () => await storage.Labs.AddAsync(TestEntities.Lab(1, 5)));

        await storage.Labs.AddAsync(TestEntities.Lab(2, 5));
        Assert.NotNull(await await storage.Labs.TryGetByPairAsync(2, 5));
    }

    [Fact]
    public async Task GetAll_ReturnsAll()
    {
        var storage = TestStorage.Create();
        await storage.Labs.AddAsync(TestEntities.Lab(1, 1));
        await storage.Labs.AddAsync(TestEntities.Lab(1, 2));
        await storage.Labs.AddAsync(TestEntities.Lab(2, 1));

        Assert.Equal(3, (await storage.Labs.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Update_SetsUpdatedAt_FromTimeProvider()
    {
        // ADR-002/FR-003 (CR-001): единственный источник бизнес-времени — TimeProvider;
        // Update проставляет UpdatedAt по FakeTimeProvider — проверка детерминирована.
        var storage = TestStorage.Create();
        var createdAt = storage.Time.GetUtcNow().UtcDateTime;
        var lab = TestEntities.Lab(1, 1);
        lab.UpdatedAt = createdAt;
        await storage.Labs.AddAsync(lab);

        var updateMoment = createdAt.AddHours(1);
        storage.Time.SetUtcNow(updateMoment);
        lab.Content = "Новое содержание";
        await storage.Labs.UpdateAsync(lab);

        var stored = (await storage.Labs.GetByIdAsync(lab.Id))!;
        Assert.Equal("Новое содержание", stored.Content);
        Assert.Equal(updateMoment, stored.UpdatedAt);
    }

    [Fact]
    public async Task Update_SamePair_Ok_OtherPair_Conflict()
    {
        var storage = TestStorage.Create();
        var first = TestEntities.Lab(1, 1);
        var second = TestEntities.Lab(1, 2);
        await storage.Labs.AddAsync(first);
        await storage.Labs.AddAsync(second);

        // Та же пара у самой записи — владелец совпадает, конфликт нет.
        first.Content = "Правка без смены пары";
        await storage.Labs.UpdateAsync(first);

        // Перенос на занятую пару другой записи — конфликт, пара не меняется.
        first.Number = 2;
        await Assert.ThrowsAsync<StorageConflictException>(async () => await storage.Labs.UpdateAsync(first));
        Assert.NotNull(await await storage.Labs.TryGetByPairAsync(1, 1));
        Assert.Equal(second.Id, (await storage.Labs.TryGetByPairAsync(1, 2))!.Id);
    }

    [Fact]
    public async Task Update_Missing_Throws()
    {
        var storage = TestStorage.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await storage.Labs.UpdateAsync(TestEntities.Lab(1, 1)));
    }

    // ------------------------------------------------------------------
    // Каскад (AC FR-002 «Каскады»): удаление работы — её сдачи удалены.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_SubmissionsOfLab_Removed_OthersKept()
    {
        var storage = TestStorage.Create();
        var removedLab = TestEntities.Lab(1, 1);
        var keptLab = TestEntities.Lab(1, 2);
        await storage.Labs.AddAsync(removedLab);
        await storage.Labs.AddAsync(keptLab);

        var student = TestEntities.User("s1");
        await storage.Users.AddAsync(student);

        var removedSubmission = await storage.Submissions.UpsertAsync(
            student.Id, removedLab.Id, new DateOnly(2026, 9, 1), null, null, DateTime.UtcNow);
        var otherSubmission = await storage.Submissions.UpsertAsync(
            student.Id, keptLab.Id, new DateOnly(2026, 9, 2), null, null, DateTime.UtcNow);

        Assert.NotNull(removedSubmission);
        Assert.NotNull(otherSubmission);

        await storage.Labs.DeleteAsync(removedLab.Id);

        Assert.Null(await await storage.Labs.GetByIdAsync(removedLab.Id));
        Assert.Null(await await storage.Labs.TryGetByPairAsync(1, 1));
        Assert.Null(await await storage.Submissions.GetByStudentAndLabAsync(student.Id, removedLab.Id));
        Assert.NotNull(await await storage.Submissions.GetByStudentAndLabAsync(student.Id, keptLab.Id));
    }

    [Fact]
    public async Task Delete_Missing_NoOp()
    {
        var storage = TestStorage.Create();
        await storage.Labs.AddAsync(TestEntities.Lab(1, 1));

        await storage.Labs.DeleteAsync(Guid.NewGuid());

        Assert.Single(await storage.Labs.GetAllAsync());
    }

    // ------------------------------------------------------------------
    // Запросные расширения T-104 (IF-015, FR-017/FR-018): ListByFilter,
    // Semesters, ExistsPair.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListByFilter_Null_ReturnsAllLabs()
    {
        var storage = TestStorage.Create();
        await storage.Labs.AddAsync(TestEntities.Lab(1, 1));
        await storage.Labs.AddAsync(TestEntities.Lab(1, 2));
        await storage.Labs.AddAsync(TestEntities.Lab(2, 1));

        Assert.Equal(3, (await storage.Labs.ListByFilterAsync(null)).Count);
    }

    [Fact]
    public async Task ListByFilter_Semester_ExactEquality_OtherSemestersExcluded()
    {
        var storage = TestStorage.Create();
        var sem1Lab1 = TestEntities.Lab(1, 1);
        var sem1Lab2 = TestEntities.Lab(1, 2);
        var sem2Lab1 = TestEntities.Lab(2, 1);
        await storage.Labs.AddAsync(sem1Lab1);
        await storage.Labs.AddAsync(sem1Lab2);
        await storage.Labs.AddAsync(sem2Lab1);

        var sem1 = await storage.Labs.ListByFilterAsync(1);
        var sem2 = await storage.Labs.ListByFilterAsync(2);

        Assert.Equal(2, sem1.Count);
        Assert.All(sem1, lab => Assert.Equal(1, lab.Semester));
        Assert.Contains(sem1, lab => lab.Id == sem1Lab1.Id);
        Assert.Contains(sem1, lab => lab.Id == sem1Lab2.Id);
        Assert.Single(sem2);
        Assert.Equal(sem2Lab1.Id, sem2[0].Id);
    }

    [Fact]
    public async Task ListByFilter_UnknownSemester_ReturnsEmpty()
    {
        var storage = TestStorage.Create();
        await storage.Labs.AddAsync(TestEntities.Lab(1, 1));

        // Внедиапазонное целое фильтра — пустая выборка, а не ошибка (ASM-018:
        // мягкая семантика; нормализация параметра — зона контроллера).
        Assert.Empty(await storage.Labs.ListByFilterAsync(99));
    }

    [Fact]
    public async Task ListByFilter_AfterDelete_ReflectsState()
    {
        var storage = TestStorage.Create();
        var lab = TestEntities.Lab(1, 1);
        await storage.Labs.AddAsync(lab);
        await storage.Labs.AddAsync(TestEntities.Lab(2, 1));

        await storage.Labs.DeleteAsync(lab.Id);

        Assert.Empty(await storage.Labs.ListByFilterAsync(1));
        Assert.Single(await storage.Labs.ListByFilterAsync(2));
        // Семестр 1 исчез вместе с последней работой, остался только семестр 2.
        Assert.Equal(2, (await storage.Labs.SemestersAsync()).Single());
    }

    [Fact]
    public async Task Semesters_DistinctAscending()
    {
        var storage = TestStorage.Create();
        await storage.Labs.AddAsync(TestEntities.Lab(2, 1));
        await storage.Labs.AddAsync(TestEntities.Lab(1, 5));
        await storage.Labs.AddAsync(TestEntities.Lab(2, 9));
        await storage.Labs.AddAsync(TestEntities.Lab(3, 1));

        Assert.Equal(new[] { 1, 2, 3 }, await storage.Labs.SemestersAsync());
    }

    [Fact]
    public async Task Semesters_EmptyStorage_ReturnsEmpty()
    {
        var storage = TestStorage.Create();

        Assert.Empty(await storage.Labs.SemestersAsync());
    }

    [Fact]
    public async Task ExistsPair_MissingPair_False_ExistingPair_True()
    {
        var storage = TestStorage.Create();
        var lab = TestEntities.Lab(1, 1);
        await storage.Labs.AddAsync(lab);

        Assert.True(await storage.Labs.ExistsPairAsync(1, 1));
        Assert.False(await storage.Labs.ExistsPairAsync(1, 2));
        Assert.False(await storage.Labs.ExistsPairAsync(2, 1));
        Assert.False(await storage.Labs.ExistsPairAsync(1, 1, lab.Id));
    }

    [Fact]
    public async Task ExistsPair_ExceptId_OwnPairNotConflict_OtherPairIs()
    {
        // Зеркало семантики Update: собственная пара (exceptId) конфликтом не считается.
        var storage = TestStorage.Create();
        var own = TestEntities.Lab(1, 1);
        var other = TestEntities.Lab(1, 2);
        await storage.Labs.AddAsync(own);
        await storage.Labs.AddAsync(other);

        Assert.False(await storage.Labs.ExistsPairAsync(1, 1, own.Id));
        Assert.True(await storage.Labs.ExistsPairAsync(1, 2, own.Id));
        Assert.True(await storage.Labs.ExistsPairAsync(1, 2, Guid.NewGuid()));
    }

    [Fact]
    public async Task ExistsPair_AfterUpdateAndDelete_TracksIndex()
    {
        var storage = TestStorage.Create();
        var lab = TestEntities.Lab(1, 1);
        await storage.Labs.AddAsync(lab);

        // Перенос пары: старая свободна, новая занята.
        lab.Semester = 2;
        lab.Number = 3;
        await storage.Labs.UpdateAsync(lab);

        Assert.False(await storage.Labs.ExistsPairAsync(1, 1));
        Assert.True(await storage.Labs.ExistsPairAsync(2, 3));

        await storage.Labs.DeleteAsync(lab.Id);

        Assert.False(await storage.Labs.ExistsPairAsync(2, 3));
    }
}
