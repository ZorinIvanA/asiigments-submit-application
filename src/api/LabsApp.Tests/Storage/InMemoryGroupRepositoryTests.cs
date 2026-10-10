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
    public void Add_AndGetByName_CaseInsensitive()
    {
        var storage = TestStorage.Create();
        var group = TestEntities.Group("ИК-221");
        storage.Groups.Add(group);

        Assert.Equal(group.Id, storage.Groups.GetById(group.Id)!.Id);
        Assert.Equal(group.Id, storage.Groups.GetByName("ик-221")!.Id);
        Assert.Equal(group.Id, storage.Groups.GetByName("ИК-221")!.Id);
    }

    [Fact]
    public void Add_DuplicateNameDifferentCase_Conflict()
    {
        var storage = TestStorage.Create();
        storage.Groups.Add(TestEntities.Group("ИК-221"));

        Assert.Throws<StorageConflictException>(
            () => storage.Groups.Add(TestEntities.Group("ик-221")));
    }

    [Fact]
    public void GetAll_ReturnsAllGroups()
    {
        var storage = TestStorage.Create();
        storage.Groups.Add(TestEntities.Group("Г1"));
        storage.Groups.Add(TestEntities.Group("Г2"));

        Assert.Equal(2, storage.Groups.GetAll().Count);
    }

    // ------------------------------------------------------------------
    // Запросные расширения T-104 (IF-015, FR-019): List и ExistsNameCi.
    // ------------------------------------------------------------------

    [Fact]
    public void List_ReturnsAllGroups_SameAsGetAll()
    {
        var storage = TestStorage.Create();
        storage.Groups.Add(TestEntities.Group("ИК-221"));
        storage.Groups.Add(TestEntities.Group("ИК-222"));

        var listed = storage.Groups.List();

        Assert.Equal(2, listed.Count);
        Assert.Equal(
            storage.Groups.GetAll().Select(group => group.Id).OrderBy(id => id),
            listed.Select(group => group.Id).OrderBy(id => id));
    }

    [Fact]
    public void List_OnEmptyStorage_ReturnsEmpty()
    {
        var storage = TestStorage.Create();

        Assert.Empty(storage.Groups.List());
    }

    [Fact]
    public void ExistsNameCi_CaseInsensitive_TrueForExisting()
    {
        var storage = TestStorage.Create();
        storage.Groups.Add(TestEntities.Group("ИК-221"));

        Assert.True(storage.Groups.ExistsNameCi("ИК-221"));
        Assert.True(storage.Groups.ExistsNameCi("ик-221"));
        Assert.True(storage.Groups.ExistsNameCi("иК-221"));
    }

    [Fact]
    public void ExistsNameCi_MissingOrAfterRename_False()
    {
        var storage = TestStorage.Create();
        var group = TestEntities.Group("Г1");
        storage.Groups.Add(group);

        Assert.False(storage.Groups.ExistsNameCi("Г2"));

        group.Name = "Г2";
        storage.Groups.Update(group);

        // Старое имя освободилось, новое занято.
        Assert.False(storage.Groups.ExistsNameCi("Г1"));
        Assert.True(storage.Groups.ExistsNameCi("г2"));
    }

    [Fact]
    public void Update_Rename_ReindexesAndRejectsConflict()
    {
        var storage = TestStorage.Create();
        var first = TestEntities.Group("Г1");
        var second = TestEntities.Group("Г2");
        storage.Groups.Add(first);
        storage.Groups.Add(second);

        // Конфликт с другой группой: изменения не сохраняются.
        first.Name = "Г2";
        Assert.Throws<StorageConflictException>(() => storage.Groups.Update(first));
        Assert.Equal("Г1", storage.Groups.GetByName("Г1")!.Name);

        // Переименование без конфликта: индексы перепривязаны.
        first.Name = "Г3";
        storage.Groups.Update(first);
        Assert.Null(storage.Groups.GetByName("Г1"));
        Assert.Equal(first.Id, storage.Groups.GetByName("г3")!.Id);
    }

    [Fact]
    public void Update_Missing_Throws()
    {
        var storage = TestStorage.Create();

        Assert.Throws<InvalidOperationException>(
            () => storage.Groups.Update(TestEntities.Group("ghost")));
    }

    // ------------------------------------------------------------------
    // Каскад (AC FR-019 «Удаление сбрасывает группу у студентов»): удаление
    // группы — студенты живы с groupId=null.
    // ------------------------------------------------------------------

    [Fact]
    public void Delete_StudentsOfGroup_AliveWithNullGroupId()
    {
        var storage = TestStorage.Create();
        var removed = TestEntities.Group("ИК-221");
        var other = TestEntities.Group("ИК-222");
        storage.Groups.Add(removed);
        storage.Groups.Add(other);

        var inRemoved1 = TestEntities.User("s1", groupId: removed.Id);
        var inRemoved2 = TestEntities.User("s2", groupId: removed.Id);
        var inOther = TestEntities.User("s3", groupId: other.Id);
        var teacher = TestEntities.User("t1", role: UserRoles.Teacher);
        storage.Users.Add(inRemoved1);
        storage.Users.Add(inRemoved2);
        storage.Users.Add(inOther);
        storage.Users.Add(teacher);

        storage.Groups.Delete(removed.Id);

        Assert.Null(storage.Groups.GetById(removed.Id));
        Assert.Null(storage.Groups.GetByName("ик-221"));
        Assert.Single(storage.Groups.GetAll());

        // Студенты удалённой группы живы, groupId сброшен.
        Assert.NotNull(storage.Users.GetById(inRemoved1.Id));
        Assert.NotNull(storage.Users.GetById(inRemoved2.Id));
        Assert.Null(storage.Users.GetById(inRemoved1.Id)!.GroupId);
        Assert.Null(storage.Users.GetById(inRemoved2.Id)!.GroupId);

        // Чужие пользователи не затронуты.
        Assert.Equal(other.Id, storage.Users.GetById(inOther.Id)!.GroupId);
        Assert.Null(storage.Users.GetById(teacher.Id)!.GroupId);
    }

    /// <summary>
    /// AC T-104 «Delete группы»: после каскада студенты живы с groupId=null,
    /// счётчики других групп не изменились, «none»-фильтр видит отвязанных.
    /// </summary>
    [Fact]
    public void Delete_DetachesStudents_OtherGroupCountsUnchanged()
    {
        var storage = TestStorage.Create();
        var ik222 = TestEntities.Group("ИК-222");
        var ik223 = TestEntities.Group("ИК-223");
        storage.Groups.Add(ik222);
        storage.Groups.Add(ik223);

        storage.Users.Add(TestEntities.User("d1", groupId: ik222.Id));
        storage.Users.Add(TestEntities.User("d2", groupId: ik222.Id));
        storage.Users.Add(TestEntities.User("k1", groupId: ik223.Id));
        storage.Users.Add(TestEntities.User("k2", groupId: ik223.Id));
        storage.Users.Add(TestEntities.User("k3", groupId: ik223.Id));

        Assert.Equal(2, storage.Users.CountByGroup(ik222.Id));
        Assert.Equal(3, storage.Users.CountByGroup(ik223.Id));

        storage.Groups.Delete(ik222.Id);

        // Студенты ИК-222 отвязаны и живы; счётчики других групп не изменились.
        Assert.Equal(0, storage.Users.CountByGroup(ik222.Id));
        Assert.Equal(3, storage.Users.CountByGroup(ik223.Id));
        Assert.Null(storage.Users.GetByLogin("d1")!.GroupId);
        Assert.Null(storage.Users.GetByLogin("d2")!.GroupId);
        Assert.Equal(2, storage.Users.ListStudents(null, "none").Count); // d1, d2
        Assert.Empty(storage.Users.ListByGroup(ik222.Id));
        Assert.Equal(3, storage.Users.ListByGroup(ik223.Id).Count);
    }

    [Fact]
    public void Delete_Missing_NoOp()
    {
        var storage = TestStorage.Create();
        storage.Groups.Add(TestEntities.Group("Г1"));

        storage.Groups.Delete(Guid.NewGuid());

        Assert.Single(storage.Groups.GetAll());
    }
}
