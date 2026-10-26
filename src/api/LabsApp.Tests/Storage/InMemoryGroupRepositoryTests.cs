using LabsApp.Domain.Entities;
using LabsApp.Storage;

namespace LabsApp.Tests.Storage;

/// <summary>
/// Юнит-проверки InMemoryGroupRepository (FR-024/IF-015): ci-уникальность имени,
/// GetAll, Update с проверкой конфликта и каскад удаления (студенты живы
/// с groupId=null, AC FR-019 «Удаление сбрасывает группу у студентов»).
/// </summary>
public sealed class InMemoryGroupRepositoryTests
{
    [Fact]
    public async Task Add_AndGetByName_CaseInsensitive()
    {
        var storage = TestStorage.Create();
        var group = TestEntities.Group("ИК-221");
        await storage.Groups.AddAsync(group);

        Assert.Equal(group.Id, (await storage.Groups.GetByIdAsync(group.Id))!.Id);
        Assert.Equal(group.Id, (await storage.Groups.GetByNameAsync("ик-221"))!.Id);
        Assert.Equal(group.Id, (await storage.Groups.GetByNameAsync("ИК-221"))!.Id);
    }

    [Fact]
    public async Task Add_DuplicateNameDifferentCase_Conflict()
    {
        var storage = TestStorage.Create();
        await storage.Groups.AddAsync(TestEntities.Group("ИК-221"));

        await Assert.ThrowsAsync<StorageConflictException>(
            async () => await storage.Groups.AddAsync(TestEntities.Group("ик-221")));
    }

    [Fact]
    public async Task GetAll_ReturnsAllGroups()
    {
        var storage = TestStorage.Create();
        await storage.Groups.AddAsync(TestEntities.Group("Г1"));
        await storage.Groups.AddAsync(TestEntities.Group("Г2"));

        Assert.Equal(2, (await storage.Groups.GetAllAsync()).Count);
    }

    // ------------------------------------------------------------------
    // Запросные расширения T-104 (IF-015, FR-019): List и ExistsNameCi.
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsAllGroups_SameAsGetAll()
    {
        var storage = TestStorage.Create();
        await storage.Groups.AddAsync(TestEntities.Group("ИК-221"));
        await storage.Groups.AddAsync(TestEntities.Group("ИК-222"));

        var listed = await storage.Groups.ListAsync();

        Assert.Equal(2, listed.Count);
        Assert.Equal(
            (await storage.Groups.GetAllAsync()).Select(group => group.Id).OrderBy(id => id),
            listed.Select(group => group.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task List_OnEmptyStorage_ReturnsEmpty()
    {
        var storage = TestStorage.Create();

        Assert.Empty(await storage.Groups.ListAsync());
    }

    [Fact]
    public async Task ExistsNameCi_CaseInsensitive_TrueForExisting()
    {
        var storage = TestStorage.Create();
        await storage.Groups.AddAsync(TestEntities.Group("ИК-221"));

        Assert.True(await storage.Groups.ExistsNameCiAsync("ИК-221"));
        Assert.True(await storage.Groups.ExistsNameCiAsync("ик-221"));
        Assert.True(await storage.Groups.ExistsNameCiAsync("иК-221"));
    }

    [Fact]
    public async Task ExistsNameCi_MissingOrAfterRename_False()
    {
        var storage = TestStorage.Create();
        var group = TestEntities.Group("Г1");
        await storage.Groups.AddAsync(group);

        Assert.False(await storage.Groups.ExistsNameCiAsync("Г2"));

        group.Name = "Г2";
        await storage.Groups.UpdateAsync(group);

        // Старое имя освободилось, новое занято.
        Assert.False(await storage.Groups.ExistsNameCiAsync("Г1"));
        Assert.True(await storage.Groups.ExistsNameCiAsync("г2"));
    }

    [Fact]
    public async Task Update_Rename_ReindexesAndRejectsConflict()
    {
        var storage = TestStorage.Create();
        var first = TestEntities.Group("Г1");
        var second = TestEntities.Group("Г2");
        await storage.Groups.AddAsync(first);
        await storage.Groups.AddAsync(second);

        // Конфликт с другой группой: изменения не сохраняются.
        first.Name = "Г2";
        await Assert.ThrowsAsync<StorageConflictException>(async () => await storage.Groups.UpdateAsync(first));
        Assert.Equal("Г1", (await storage.Groups.GetByNameAsync("Г1"))!.Name);

        // Переименование без конфликта: индексы перепривязаны.
        first.Name = "Г3";
        await storage.Groups.UpdateAsync(first);
        Assert.Null(await await storage.Groups.GetByNameAsync("Г1"));
        Assert.Equal(first.Id, (await storage.Groups.GetByNameAsync("г3"))!.Id);
    }

    [Fact]
    public async Task Update_Missing_Throws()
    {
        var storage = TestStorage.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await storage.Groups.UpdateAsync(TestEntities.Group("ghost")));
    }

    // ------------------------------------------------------------------
    // Каскад (AC FR-019 «Удаление сбрасывает группу у студентов»): удаление
    // группы — студенты живы с groupId=null.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_StudentsOfGroup_AliveWithNullGroupId()
    {
        var storage = TestStorage.Create();
        var removed = TestEntities.Group("ИК-221");
        var other = TestEntities.Group("ИК-222");
        await storage.Groups.AddAsync(removed);
        await storage.Groups.AddAsync(other);

        var inRemoved1 = TestEntities.User("s1", groupId: removed.Id);
        var inRemoved2 = TestEntities.User("s2", groupId: removed.Id);
        var inOther = TestEntities.User("s3", groupId: other.Id);
        var teacher = TestEntities.User("t1", role: UserRoles.Teacher);
        await storage.Users.AddAsync(inRemoved1);
        await storage.Users.AddAsync(inRemoved2);
        await storage.Users.AddAsync(inOther);
        await storage.Users.AddAsync(teacher);

        await storage.Groups.DeleteAsync(removed.Id);

        Assert.Null(await await storage.Groups.GetByIdAsync(removed.Id));
        Assert.Null(await await storage.Groups.GetByNameAsync("ик-221"));
        Assert.Single(await storage.Groups.GetAllAsync());

        // Студенты удалённой группы живы, groupId сброшен.
        Assert.NotNull(await await storage.Users.GetByIdAsync(inRemoved1.Id));
        Assert.NotNull(await await storage.Users.GetByIdAsync(inRemoved2.Id));
        Assert.Null((await storage.Users.GetByIdAsync(inRemoved1.Id))!.GroupId);
        Assert.Null((await storage.Users.GetByIdAsync(inRemoved2.Id))!.GroupId);

        // Чужие пользователи не затронуты.
        Assert.Equal(other.Id, (await storage.Users.GetByIdAsync(inOther.Id))!.GroupId);
        Assert.Null((await storage.Users.GetByIdAsync(teacher.Id))!.GroupId);
    }

    /// <summary>
    /// AC T-104 «Delete группы»: после каскада студенты живы с groupId=null,
    /// счётчики других групп не изменились, «none»-фильтр видит отвязанных.
    /// </summary>
    [Fact]
    public async Task Delete_DetachesStudents_OtherGroupCountsUnchanged()
    {
        var storage = TestStorage.Create();
        var ik222 = TestEntities.Group("ИК-222");
        var ik223 = TestEntities.Group("ИК-223");
        await storage.Groups.AddAsync(ik222);
        await storage.Groups.AddAsync(ik223);

        await storage.Users.AddAsync(TestEntities.User("d1", groupId: ik222.Id));
        await storage.Users.AddAsync(TestEntities.User("d2", groupId: ik222.Id));
        await storage.Users.AddAsync(TestEntities.User("k1", groupId: ik223.Id));
        await storage.Users.AddAsync(TestEntities.User("k2", groupId: ik223.Id));
        await storage.Users.AddAsync(TestEntities.User("k3", groupId: ik223.Id));

        Assert.Equal(2, await storage.Users.CountByGroupAsync(ik222.Id));
        Assert.Equal(3, await storage.Users.CountByGroupAsync(ik223.Id));

        await storage.Groups.DeleteAsync(ik222.Id);

        // Студенты ИК-222 отвязаны и живы; счётчики других групп не изменились.
        Assert.Equal(0, await storage.Users.CountByGroupAsync(ik222.Id));
        Assert.Equal(3, await storage.Users.CountByGroupAsync(ik223.Id));
        Assert.Null((await storage.Users.GetByLoginAsync("d1"))!.GroupId);
        Assert.Null((await storage.Users.GetByLoginAsync("d2"))!.GroupId);
        Assert.Equal(2, (await storage.Users.ListStudentsAsync(null, "none")).Count); // d1, d2
        Assert.Empty(await storage.Users.ListByGroupAsync(ik222.Id));
        Assert.Equal(3, (await storage.Users.ListByGroupAsync(ik223.Id)).Count);
    }

    [Fact]
    public async Task Delete_Missing_NoOp()
    {
        var storage = TestStorage.Create();
        await storage.Groups.AddAsync(TestEntities.Group("Г1"));

        await storage.Groups.DeleteAsync(Guid.NewGuid());

        Assert.Single(await storage.Groups.GetAllAsync());
    }
}
