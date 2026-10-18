using System.Collections.Concurrent;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;

namespace LabsApp.Tests.Storage;

/// <summary>
/// Юнит-проверки InMemoryUserRepository (FR-024/IF-015): ci-уникальность
/// логина/email при разном регистре, атомарность «проверка + вставка»,
/// перепривязка индексов при Update, ListStudents, потокобезопасность
/// (≥100 параллельных созданий из нескольких потоков).
/// </summary>
public sealed class InMemoryUserRepositoryTests
{
    [Fact]
    public void Add_AndFindByLoginAndEmail_CaseInsensitive()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        var user = TestEntities.User("Ivanov", "Ivanov@Example.COM");
        users.Add(user);

        Assert.Equal(user.Id, users.GetById(user.Id)!.Id);
        Assert.Equal(user.Id, users.GetByLogin("ivanov")!.Id);
        Assert.Equal(user.Id, users.GetByLogin("IVANOV")!.Id);
        Assert.Equal(user.Id, users.GetByEmail("ivanov@example.com")!.Id);
        Assert.Equal(user.Id, users.GetByEmail("IVANOV@EXAMPLE.COM")!.Id);
    }

    [Fact]
    public void Add_ReturnsSnapshot_ExternalMutationNotTracked()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        var user = TestEntities.User("a", "a@example.com");
        users.Add(user);

        // Хранилище отдаёт копии-снимки: мутация копии не влияет на хранилище.
        user.FullName = "Испорчено снаружи";
        Assert.NotEqual("Испорчено снаружи", users.GetByLogin("a")!.FullName);
    }

    [Fact]
    public void Add_DuplicateLoginDifferentCase_Conflict_UserNotInserted()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        users.Add(TestEntities.User("student01", "a@example.com"));

        var conflict = Record.Exception(() =>
            users.Add(TestEntities.User("STUDENT01", "other@example.com")));

        Assert.IsType<StorageConflictException>(conflict);
        Assert.Null(users.GetByEmail("other@example.com"));
        Assert.Single(users.ListStudents());
        Assert.NotNull(users.GetByLogin("student01"));
    }

    [Fact]
    public void Add_DuplicateEmailDifferentCase_Conflict()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        users.Add(TestEntities.User("a", "Mail@Example.com"));

        Assert.Throws<StorageConflictException>(
            () => users.Add(TestEntities.User("b", "mail@example.COM")));
    }

    [Fact]
    public void GetById_Missing_ReturnsNull()
    {
        var users = new InMemoryUserRepository(new StorageLock());

        Assert.Null(users.GetById(Guid.NewGuid()));
    }

    [Fact]
    public void Update_ChangedEmail_Reindexes()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        var user = TestEntities.User("a", "old@example.com");
        users.Add(user);

        user.Email = "new@example.com";
        users.Update(user);

        Assert.Null(users.GetByEmail("old@example.com"));
        Assert.Equal(user.Id, users.GetByEmail("new@example.com")!.Id);
        Assert.Equal(user.Id, users.GetByLogin("a")!.Id);
    }

    [Fact]
    public void Update_ChangedLogin_Reindexes()
    {
        // Смена логина на свободный: ветка «логин не занят» проходит без
        // конфликта, ci-индекс login перепривязывается.
        var users = new InMemoryUserRepository(new StorageLock());
        var user = TestEntities.User("old-login", "a@example.com");
        users.Add(user);

        user.Login = "new-login";
        users.Update(user);

        Assert.Null(users.GetByLogin("old-login"));
        Assert.Equal(user.Id, users.GetByLogin("new-login")!.Id);
        Assert.Equal(user.Id, users.GetByEmail("a@example.com")!.Id);
    }

    [Fact]
    public void Update_SameLoginAndEmail_Succeeds()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        var user = TestEntities.User("a", "a@example.com");
        users.Add(user);

        user.FullName = "Другое ФИО";
        users.Update(user);

        Assert.Equal("Другое ФИО", users.GetByLogin("a")!.FullName);
    }

    [Fact]
    public void Update_EmailOfAnotherUser_Conflict_NoChanges()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        var first = TestEntities.User("a", "a@example.com");
        var second = TestEntities.User("b", "b@example.com");
        users.Add(first);
        users.Add(second);

        first.Email = "b@example.com";
        var conflict = Record.Exception(() => users.Update(first));

        Assert.IsType<StorageConflictException>(conflict);
        Assert.Equal("a@example.com", users.GetByLogin("a")!.Email);
        Assert.Equal("b@example.com", users.GetByLogin("b")!.Email);
    }

    [Fact]
    public void Update_LoginOfAnotherUser_Conflict_NoChanges()
    {
        // Зеркало Update_EmailOfAnotherUser_Conflict_NoChanges для ветки
        // конфликта Login (CONTRACT CONFLICT, IF-015): изменения не сохраняются.
        var users = new InMemoryUserRepository(new StorageLock());
        var first = TestEntities.User("a", "a@example.com");
        var second = TestEntities.User("b", "b@example.com");
        users.Add(first);
        users.Add(second);

        first.Login = "b";
        var conflict = Record.Exception(() => users.Update(first));

        Assert.IsType<StorageConflictException>(conflict);
        Assert.Equal("a", users.GetByEmail("a@example.com")!.Login);
        Assert.Equal("b", users.GetByEmail("b@example.com")!.Login);
        Assert.Equal(first.Id, users.GetByLogin("a")!.Id);
        Assert.Equal(second.Id, users.GetByLogin("b")!.Id);
    }

    [Fact]
    public void Update_MissingUser_Throws()
    {
        var users = new InMemoryUserRepository(new StorageLock());

        Assert.Throws<InvalidOperationException>(
            () => users.Update(TestEntities.User("ghost")));
    }

    [Fact]
    public void ListStudents_ReturnsOnlyStudents()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        users.Add(TestEntities.User("teacher", role: UserRoles.Teacher));
        users.Add(TestEntities.User("s1"));
        users.Add(TestEntities.User("s2"));

        var students = users.ListStudents();

        Assert.Equal(2, students.Count);
        Assert.All(students, student => Assert.Equal(UserRoles.Student, student.Role));
    }

    // ------------------------------------------------------------------
    // Многословный одно-полевой ci-поиск и фильтр группы (FR-020, T-104).
    // ------------------------------------------------------------------

    [Fact]
    public void ListStudents_MultiWordSearch_AllTokensInFullName_Finds()
    {
        // AC T-104 «Многословный поиск», часть 1: оба токена — подстроки fullName.
        var users = new InMemoryUserRepository(new StorageLock());
        var student01 = TestEntities.User(
            "student01",
            fullName: "Иванов Иван Иванович 01");
        users.Add(student01);
        users.Add(TestEntities.User("student02", fullName: "Петров Пётр Петрович 02"));

        var found = users.ListStudents("иван 01");

        Assert.Single(found);
        Assert.Equal(student01.Id, found[0].Id);
    }

    [Fact]
    public void ListStudents_MultiWordSearch_TokensInDifferentFields_NoMatch()
    {
        // AC T-104 «Многословный поиск», часть 2: «иванов» — только в fullName,
        // «student01@example.com» — только в email; ОДНОГО поля со всеми токенами нет.
        var users = new InMemoryUserRepository(new StorageLock());
        users.Add(TestEntities.User(
            "student01",
            fullName: "Иванов Иван Иванович 01"));

        Assert.Empty(users.ListStudents("иванов student01@example.com"));
    }

    [Fact]
    public void ListStudents_MultiWordSearch_TokenOrderIrrelevant()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        var student01 = TestEntities.User("student01", fullName: "Иванов Иван Иванович 01");
        users.Add(student01);

        var found = users.ListStudents("01 иван");

        Assert.Single(found);
        Assert.Equal(student01.Id, found[0].Id);
    }

    [Fact]
    public void ListStudents_Search_CaseInsensitive()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        var student01 = TestEntities.User("Student01", fullName: "Иванов Иван Иванович 01");
        users.Add(student01);

        var byUpperQuery = users.ListStudents("ИВАНОВ");
        var byMixedLogin = users.ListStudents("sTUDENT01");

        Assert.Single(byUpperQuery);
        Assert.Equal(student01.Id, byUpperQuery[0].Id);
        Assert.Single(byMixedLogin);
        Assert.Equal(student01.Id, byMixedLogin[0].Id);
    }

    [Fact]
    public void ListStudents_SingleToken_MatchesLoginOrEmail()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        var student01 = TestEntities.User("student01", "student01@example.com");
        users.Add(student01);
        users.Add(TestEntities.User("petrov", fullName: "Иванов Пётр"));

        // Токен-подстрока login и email student01 — находится в каждом из полей.
        Assert.Equal(student01.Id, users.ListStudents("student01").Single().Id);
        Assert.Equal(student01.Id, users.ListStudents("STUDENT01@EXAMPLE.COM").Single().Id);
        // Частичный токен (подстрока email) — тоже находит student01.
        Assert.Equal(student01.Id, users.ListStudents("student01@example.co").Single().Id);
        // Ничья подстрока — пусто.
        Assert.Empty(users.ListStudents("student02@example.com"));
    }

    [Fact]
    public void ListStudents_Search_EmptyOrWhitespace_NoFilter()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        users.Add(TestEntities.User("s1", fullName: "Иванов Иван"));
        users.Add(TestEntities.User("s2", fullName: "Петров Пётр"));

        Assert.Equal(2, users.ListStudents(null).Count);
        Assert.Equal(2, users.ListStudents(string.Empty).Count);
        Assert.Equal(2, users.ListStudents("   ").Count);
    }

    [Fact]
    public void ListStudents_Search_PaddedQuery_TrimmedTokensStillMatch()
    {
        // Требование T-104 (unit_test_requirements «трим»): крайние и повторные
        // пробелы не порождают лишних токенов — «  иван   01  » ≡ «иван 01».
        var users = new InMemoryUserRepository(new StorageLock());
        var student01 = TestEntities.User("student01", fullName: "Иванов Иван Иванович 01");
        users.Add(student01);
        users.Add(TestEntities.User("student02", fullName: "Петров Пётр 01"));

        var found = users.ListStudents("  иван   01  ");

        Assert.Single(found);
        Assert.Equal(student01.Id, found[0].Id);
    }

    [Fact]
    public void ListStudents_Search_TeachersExcluded_EvenIfFullNameMatches()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        users.Add(TestEntities.User("t1", role: UserRoles.Teacher, fullName: "Иванов Учитель Учителевич"));

        Assert.Empty(users.ListStudents("иванов"));
    }

    [Fact]
    public void ListStudents_GroupFilterNone_ReturnsOnlyUngrouped()
    {
        // AC T-104 «Фильтр группы», часть 1: ListStudents(null, 'none').
        var users = new InMemoryUserRepository(new StorageLock());
        var group = TestEntities.Group("ИК-221");
        users.Add(TestEntities.User("g1", groupId: group.Id));
        users.Add(TestEntities.User("g2", groupId: group.Id));
        users.Add(TestEntities.User("free1"));
        users.Add(TestEntities.User("free2"));
        users.Add(TestEntities.User("t1", role: UserRoles.Teacher));

        var ungrouped = users.ListStudents(null, "none");

        Assert.Equal(2, ungrouped.Count);
        Assert.All(ungrouped, student => Assert.Null(student.GroupId));
    }

    [Fact]
    public void ListStudents_GroupFilterAbsent_NoGroupFiltering()
    {
        // AC T-104 «Фильтр группы»: null/пустой groupIdFilter — без фильтра:
        // и состоящие в группах, и свободные студенты в выдаче.
        var users = new InMemoryUserRepository(new StorageLock());
        var group = TestEntities.Group("ИК-221");
        users.Add(TestEntities.User("g1", groupId: group.Id));
        users.Add(TestEntities.User("free1"));
        users.Add(TestEntities.User("t1", role: UserRoles.Teacher));

        Assert.Equal(2, users.ListStudents(null, null).Count);
        Assert.Equal(2, users.ListStudents(null, string.Empty).Count);
        Assert.Equal(2, users.ListStudents(null, "   ").Count);
    }

    [Fact]
    public void ListStudents_GroupFilterByUuid_ReturnsOnlyThatGroup()
    {
        // AC T-104 «Фильтр группы», часть 2: ListStudents(null, <uuid ИК-221>).
        var users = new InMemoryUserRepository(new StorageLock());
        var ik221 = TestEntities.Group("ИК-221");
        var ik222 = TestEntities.Group("ИК-222");
        var in221 = TestEntities.User("in221", groupId: ik221.Id);
        users.Add(in221);
        users.Add(TestEntities.User("in222", groupId: ik222.Id));
        users.Add(TestEntities.User("free"));

        var found = users.ListStudents(null, ik221.Id.ToString());

        Assert.Single(found);
        Assert.Equal(in221.Id, found[0].Id);
    }

    [Fact]
    public void ListStudents_GroupFilterUnknownUuid_EmptyResult_NotError()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        users.Add(TestEntities.User("s1", groupId: TestEntities.Group("ИК-221").Id));

        Assert.Empty(users.ListStudents(null, Guid.NewGuid().ToString()));
    }

    [Fact]
    public void ListStudents_GroupFilterNotUuidAndNotNone_EmptyResult()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        users.Add(TestEntities.User("free"));

        Assert.Empty(users.ListStudents(null, "не-uuid"));
    }

    [Fact]
    public void ListStudents_SearchAndGroupFilter_Combine()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        var group = TestEntities.Group("ИК-221");
        users.Add(TestEntities.User("s1", fullName: "Иванов Иван", groupId: group.Id));
        users.Add(TestEntities.User("s2", fullName: "Иванов Пётр", groupId: group.Id));
        users.Add(TestEntities.User("s3", fullName: "Иванов Андрей"));

        var found = users.ListStudents("иван", group.Id.ToString());

        Assert.Equal(2, found.Count);
        Assert.All(found, student => Assert.Equal(group.Id, student.GroupId));
    }

    [Fact]
    public void ListByGroup_ReturnsOnlyStudentsOfGroup()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        var ik221 = TestEntities.Group("ИК-221");
        var ik222 = TestEntities.Group("ИК-222");
        var first = TestEntities.User("a", groupId: ik221.Id);
        var second = TestEntities.User("b", groupId: ik221.Id);
        users.Add(first);
        users.Add(second);
        users.Add(TestEntities.User("c", groupId: ik222.Id));
        users.Add(TestEntities.User("free"));

        var byGroup = users.ListByGroup(ik221.Id);

        Assert.Equal(2, byGroup.Count);
        Assert.Contains(byGroup, student => student.Id == first.Id);
        Assert.Contains(byGroup, student => student.Id == second.Id);
        Assert.Empty(users.ListByGroup(Guid.NewGuid()));
    }

    [Fact]
    public void CountByGroup_CountsStudentsOfGroup()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        var ik221 = TestEntities.Group("ИК-221");
        var ik222 = TestEntities.Group("ИК-222");
        users.Add(TestEntities.User("a", groupId: ik221.Id));
        users.Add(TestEntities.User("b", groupId: ik221.Id));
        users.Add(TestEntities.User("c", groupId: ik222.Id));

        Assert.Equal(2, users.CountByGroup(ik221.Id));
        Assert.Equal(1, users.CountByGroup(ik222.Id));
        Assert.Equal(0, users.CountByGroup(Guid.NewGuid()));
    }

    // ------------------------------------------------------------------
    // Узкая атомарная мутация группы (SEC-001): SetGroup меняет ТОЛЬКО GroupId
    // в одной критической секции — проверки студента и группы до записи, полный
    // снимок записи не перезаписывается.
    // ------------------------------------------------------------------

    [Fact]
    public void SetGroup_Assign_OnlyGroupIdChanges_RestOfRecordUntouched()
    {
        // SEC-001: смена группы не перечитывает и не перезаписывает остальные поля
        // записи — конкурентная смена пароля/профиля не может быть откатана.
        var users = new InMemoryUserRepository(new StorageLock());
        var student = TestEntities.User("s1", fullName: "Старое ФИО");
        student.PasswordHash = "hash-v1";
        student.CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        users.Add(student);
        var group = TestEntities.Group("ИК-223");

        var result = users.SetGroup(student.Id, group.Id, groupId => groupId == group.Id);

        Assert.Equal(SetGroupResult.Success, result);
        var stored = users.GetById(student.Id)!;
        Assert.Equal(group.Id, stored.GroupId);
        Assert.Equal("hash-v1", stored.PasswordHash);
        Assert.Equal("Старое ФИО", stored.FullName);
        Assert.Equal("s1", stored.Login);
        Assert.Equal("s1@example.com", stored.Email);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), stored.CreatedAt);
    }

    [Fact]
    public void SetGroup_Unassign_ClearsGroupId_NoDelegateCall()
    {
        // JsonNull (снятие группы): проверка группы не выполняется — делегат не вызывается.
        var users = new InMemoryUserRepository(new StorageLock());
        var group = TestEntities.Group("ИК-223");
        var student = TestEntities.User("s1", groupId: group.Id);
        users.Add(student);
        var delegateCalls = 0;

        var result = users.SetGroup(student.Id, null, _ =>
        {
            delegateCalls++;
            return true;
        });

        Assert.Equal(SetGroupResult.Success, result);
        Assert.Equal(0, delegateCalls);
        Assert.Null(users.GetById(student.Id)!.GroupId);
    }

    [Fact]
    public void SetGroup_Transfer_OldGroupReleased_NewGroupSet()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        var ik221 = TestEntities.Group("ИК-221");
        var ik222 = TestEntities.Group("ИК-222");
        users.Add(TestEntities.User("s1", groupId: ik221.Id));

        var result = users.SetGroup(
            users.GetByLogin("s1")!.Id,
            ik222.Id,
            groupId => groupId == ik222.Id);

        Assert.Equal(SetGroupResult.Success, result);
        Assert.Equal(ik222.Id, users.GetByLogin("s1")!.GroupId);
        Assert.Equal(0, users.CountByGroup(ik221.Id));
        Assert.Equal(1, users.CountByGroup(ik222.Id));
    }

    [Fact]
    public void SetGroup_MissingUser_StudentNotFound()
    {
        var users = new InMemoryUserRepository(new StorageLock());

        Assert.Equal(SetGroupResult.StudentNotFound, users.SetGroup(Guid.NewGuid(), null, null));
        Assert.Equal(
            SetGroupResult.StudentNotFound,
            users.SetGroup(Guid.NewGuid(), Guid.NewGuid(), _ => true));
    }

    [Fact]
    public void SetGroup_TeacherRole_StudentNotFound_RecordUntouched()
    {
        // Роль ≠ student приравнена к отсутствию (IF-011) — в обеих ветках (String и JsonNull).
        var users = new InMemoryUserRepository(new StorageLock());
        var group = TestEntities.Group("ИК-223");
        var teacher = TestEntities.User("t1", role: UserRoles.Teacher);
        users.Add(teacher);

        Assert.Equal(SetGroupResult.StudentNotFound, users.SetGroup(teacher.Id, group.Id, _ => true));
        Assert.Equal(SetGroupResult.StudentNotFound, users.SetGroup(teacher.Id, null, null));
        Assert.Null(users.GetById(teacher.Id)!.GroupId);
    }

    [Fact]
    public void SetGroup_UnknownGroup_GroupNotFound_RecordUnchanged()
    {
        // Проверка группы и запись — одна критическая секция (SEC-001): группа,
        // не прошедшая проверку, не оставляет студенту ни новой, ни висячей группы.
        var users = new InMemoryUserRepository(new StorageLock());
        var ik221 = TestEntities.Group("ИК-221");
        var student = TestEntities.User("s1", groupId: ik221.Id);
        users.Add(student);

        var result = users.SetGroup(student.Id, Guid.NewGuid(), _ => false);

        Assert.Equal(SetGroupResult.GroupNotFound, result);
        Assert.Equal(ik221.Id, users.GetById(student.Id)!.GroupId);
    }

    [Fact]
    public void SetGroup_NonNullGroupWithNullDelegate_ThrowsArgumentNull_NoChanges()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        var student = TestEntities.User("s1");
        users.Add(student);

        Assert.Throws<ArgumentNullException>(
            () => users.SetGroup(student.Id, Guid.NewGuid(), null));
        Assert.Null(users.GetById(student.Id)!.GroupId);
    }

    [Fact]
    public void SetGroup_GroupDelegate_MayReadStorageUnderSameLock()
    {
        // Делегат вызывается под блокировкой мутации: единый замок реентерабелен,
        // чтение хранилища из делегата допустимо и видит состояние ДО записи.
        var users = new InMemoryUserRepository(new StorageLock());
        var student = TestEntities.User("s1");
        users.Add(student);
        var group = TestEntities.Group("ИК-223");

        var result = users.SetGroup(
            student.Id,
            group.Id,
            _ => users.GetById(student.Id) is { GroupId: null });

        Assert.Equal(SetGroupResult.Success, result);
        Assert.Equal(group.Id, users.GetById(student.Id)!.GroupId);
    }

    // ------------------------------------------------------------------
    // Узкая атомарная мутация пароля (CR-001/SEC-001): SetPassword меняет
    // ТОЛЬКО PasswordHash в одной критической секции; остальные поля записи
    // не перечитываются и не перезаписываются, ci-индексы не затрагиваются.
    // ------------------------------------------------------------------

    [Fact]
    public void SetPassword_OnlyPasswordHashChanges_FullSnapshotCompared()
    {
        // AC T-204 «Узость SetPassword»: сравнение ПОЛНЫХ снимков записи до/после —
        // изменился ровно один член (PasswordHash).
        var users = new InMemoryUserRepository(new StorageLock());
        var group = TestEntities.Group("ИК-221");
        var student = TestEntities.User("s1", "S1@Example.com", groupId: group.Id);
        student.PasswordHash = "hash-v1";
        student.CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        users.Add(student);
        var before = users.GetById(student.Id)!;

        var result = users.SetPassword(student.Id, "hash-v2");

        Assert.True(result);
        var after = users.GetById(student.Id)!;
        Assert.Equal("hash-v2", after.PasswordHash);
        Assert.NotEqual(before.PasswordHash, after.PasswordHash);
        // Остальные члены снимка — дословно прежние.
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Login, after.Login);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.FullName, after.FullName);
        Assert.Equal(before.Role, after.Role);
        Assert.Equal(before.GroupId, after.GroupId);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
    }

    [Fact]
    public void SetPassword_KeepsByLoginAndByEmailLookupWorking()
    {
        // AC T-204 «Узость SetPassword»: ci-индексы не трогаются — запись находится
        // по login и email в обоих регистрах и после смены пароля.
        var users = new InMemoryUserRepository(new StorageLock());
        var student = TestEntities.User("s1", "S1@Example.com");
        users.Add(student);

        Assert.True(users.SetPassword(student.Id, "hash-v2"));

        Assert.Equal(student.Id, users.GetByLogin("s1")!.Id);
        Assert.Equal(student.Id, users.GetByLogin("S1")!.Id);
        Assert.Equal(student.Id, users.GetByEmail("s1@example.com")!.Id);
        Assert.Equal(student.Id, users.GetByEmail("S1@EXAMPLE.COM")!.Id);
        Assert.Equal("hash-v2", users.GetByLogin("s1")!.PasswordHash);
    }

    [Fact]
    public void SetPassword_MissingUser_ReturnsFalse_NoException()
    {
        // AC T-204 «Узость SetPassword»: несуществующий userId → false без исключения.
        var users = new InMemoryUserRepository(new StorageLock());

        var result = users.SetPassword(Guid.NewGuid(), "hash-v2");

        Assert.False(result);
    }

    [Fact]
    public void SetPassword_NullOrEmptyHash_ThrowsArgumentNull_NoChanges()
    {
        // Контракт IF-015 (валидация аргументов вне критической секции).
        var users = new InMemoryUserRepository(new StorageLock());
        var student = TestEntities.User("s1");
        users.Add(student);

        Assert.Throws<ArgumentNullException>(() => users.SetPassword(student.Id, null!));
        Assert.Throws<ArgumentNullException>(() => users.SetPassword(student.Id, string.Empty));
        Assert.Equal("hash", users.GetById(student.Id)!.PasswordHash);
    }

    // ------------------------------------------------------------------
    // Узкая атомарная мутация профиля (CR-001/SEC-001): UpdateProfile меняет
    // ТОЛЬКО FullName и Email с атомарной ci-проверкой занятости email ДРУГИМ
    // пользователем и перебинтовкой byEmail в той же критической секции.
    // ------------------------------------------------------------------

    [Fact]
    public void UpdateProfile_Success_OnlyFullNameAndEmailChange_ReindexesByEmail()
    {
        // AC T-204 «UpdateProfile: конфликты и перебинтовка» (успешная ветка):
        // новый email находит запись, старый — null; остальные члены снимка прежние.
        var users = new InMemoryUserRepository(new StorageLock());
        var group = TestEntities.Group("ИК-221");
        var student = TestEntities.User("s1", "old@example.com", groupId: group.Id);
        student.PasswordHash = "hash-v1";
        student.CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        users.Add(student);

        var result = users.UpdateProfile(student.Id, "Новое ФИО", "new@example.com");

        Assert.Equal(UpdateProfileResult.Success, result);
        var after = users.GetById(student.Id)!;
        Assert.Equal("Новое ФИО", after.FullName);
        Assert.Equal("new@example.com", after.Email);
        // Перебинтовка byEmail: старый ключ освобождён, новый указывает на запись.
        Assert.Null(users.GetByEmail("old@example.com"));
        Assert.Equal(student.Id, users.GetByEmail("new@example.com")!.Id);
        Assert.Equal(student.Id, users.GetByEmail("NEW@EXAMPLE.COM")!.Id);
        // Остальные члены записи — дословно прежние.
        Assert.Equal("s1", after.Login);
        Assert.Equal("hash-v1", after.PasswordHash);
        Assert.Equal(UserRoles.Student, after.Role);
        Assert.Equal(group.Id, after.GroupId);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), after.CreatedAt);
        // byLogin не затронут.
        Assert.Equal(student.Id, users.GetByLogin("s1")!.Id);
    }

    [Fact]
    public void UpdateProfile_ForeignEmail_EmailConflict_RecordUntouched()
    {
        // AC T-204 «UpdateProfile: конфликты и перебинтовка» (ветка конфликта):
        // email B занят — EmailConflict, запись A не изменена (полный снимок).
        var users = new InMemoryUserRepository(new StorageLock());
        var first = TestEntities.User("a", "a@example.com", fullName: "ФИО A");
        var second = TestEntities.User("b", "b@example.com");
        users.Add(first);
        users.Add(second);
        var before = users.GetById(first.Id)!;

        var result = users.UpdateProfile(first.Id, "Новое ФИО", "b@example.com");
        var caseVariant = users.UpdateProfile(first.Id, "Новое ФИО", "B@EXAMPLE.COM");

        Assert.Equal(UpdateProfileResult.EmailConflict, result);
        Assert.Equal(UpdateProfileResult.EmailConflict, caseVariant);
        var after = users.GetById(first.Id)!;
        Assert.Equal(before.FullName, after.FullName);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.GroupId, after.GroupId);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        // Индексы согласованы: A по-прежнему по своему email.
        Assert.Equal(first.Id, users.GetByEmail("a@example.com")!.Id);
        Assert.Equal(second.Id, users.GetByEmail("b@example.com")!.Id);
    }

    [Fact]
    public void UpdateProfile_OwnEmail_NotConflict_IncludingCaseVariant()
    {
        // FR-015 «Свой email не конфликтует»: совпадение с самим собой (в т.ч.
        // ci-вариант регистра) — Success; ci-ключ не меняется, хранится новая
        // литеральная запись email.
        var users = new InMemoryUserRepository(new StorageLock());
        var student = TestEntities.User("s1", "s1@example.com");
        users.Add(student);

        Assert.Equal(
            UpdateProfileResult.Success,
            users.UpdateProfile(student.Id, "ФИО", "s1@example.com"));
        Assert.Equal(
            UpdateProfileResult.Success,
            users.UpdateProfile(student.Id, "ФИО", "S1@Example.COM"));

        var stored = users.GetById(student.Id)!;
        Assert.Equal("S1@Example.COM", stored.Email);
        Assert.Equal(student.Id, users.GetByEmail("s1@example.com")!.Id);
        Assert.Equal(student.Id, users.GetByEmail("S1@EXAMPLE.com")!.Id);
    }

    [Fact]
    public void UpdateProfile_MissingUser_UserNotFound()
    {
        // AC T-204 «UpdateProfile: конфликты и перебинтовка» (ветка отсутствия):
        // несуществующий userId → UserNotFound без исключения.
        var users = new InMemoryUserRepository(new StorageLock());

        Assert.Equal(
            UpdateProfileResult.UserNotFound,
            users.UpdateProfile(Guid.NewGuid(), "ФИО", "free@example.com"));
    }

    [Fact]
    public void UpdateProfile_NullFullNameOrNullEmail_ThrowsArgumentNull_NoChanges()
    {
        // Контракт IF-015 (валидация аргументов вне критической секции).
        var users = new InMemoryUserRepository(new StorageLock());
        var student = TestEntities.User("s1");
        users.Add(student);

        Assert.Throws<ArgumentNullException>(
            () => users.UpdateProfile(student.Id, null!, "free@example.com"));
        Assert.Throws<ArgumentNullException>(
            () => users.UpdateProfile(student.Id, "ФИО", null!));
        Assert.Equal("Тест Тестович Тестов", users.GetById(student.Id)!.FullName);
        Assert.Equal("s1@example.com", users.GetById(student.Id)!.Email);
    }

    // ------------------------------------------------------------------
    // Параллельность узких мутаторов (CR-001, образец — StorageConcurrencyGateTests):
    // SetPassword ∥ SetGroup ∥ UpdateProfile на ОДНОЙ записи — без потери
    // обновлений соседних полей; гонка двух UpdateProfile на один email —
    // ровно один победитель.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ParallelNarrowMutators_OnOneRecord_NoLostUpdates()
    {
        // Барьерная синхронизация раундов: в каждом раунде i все три мутатора
        // пишут значения i. После финального раунда КАЖДОЕ поле обязано хранить
        // значение последнего раунда — полная замена устаревшим снимком откатила
        // бы соседние поля к более раннему раунду и провалила бы гейт.
        const int rounds = 200;
        var users = new InMemoryUserRepository(new StorageLock());
        var groupA = TestEntities.Group("ИК-221");
        var student = TestEntities.User("s1", "s1@example.com");
        student.PasswordHash = "hash-initial";
        users.Add(student);

        using var barrier = new Barrier(3);
        var failures = new ConcurrentQueue<Exception>();

        // Сбой внутри раунда (Assert/mутатор) не прерывает участие в барьере —
        // иначе остальные потоки зависли бы на SignalAndWait; факт сбоя
        // финализируется после прогона.
        var setPassword = Task.Run(() =>
        {
            for (var i = 0; i < rounds; i++)
            {
                try
                {
                    if (!users.SetPassword(student.Id, $"hash-{i:000}"))
                    {
                        failures.Enqueue(new InvalidOperationException("SetPassword вернул false."));
                    }
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }

                barrier.SignalAndWait();
            }
        });
        var setGroup = Task.Run(() =>
        {
            for (var i = 0; i < rounds; i++)
            {
                try
                {
                    if (users.SetGroup(student.Id, groupA.Id, _ => true) != SetGroupResult.Success)
                    {
                        failures.Enqueue(new InvalidOperationException("SetGroup не Success."));
                    }
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }

                barrier.SignalAndWait();
            }
        });
        var updateProfile = Task.Run(() =>
        {
            for (var i = 0; i < rounds; i++)
            {
                try
                {
                    if (users.UpdateProfile(student.Id, $"ФИО-{i:000}", $"s1-{i:000}@example.com")
                        != UpdateProfileResult.Success)
                    {
                        failures.Enqueue(new InvalidOperationException("UpdateProfile не Success."));
                    }
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }

                barrier.SignalAndWait();
            }
        });

        await Task.WhenAll(setPassword, setGroup, updateProfile);
        Assert.Empty(failures);

        var stored = users.GetById(student.Id)!;
        Assert.Equal($"hash-{rounds - 1:000}", stored.PasswordHash);
        Assert.Equal($"ФИО-{rounds - 1:000}", stored.FullName);
        Assert.Equal($"s1-{rounds - 1:000}@example.com", stored.Email);
        Assert.Equal(groupA.Id, stored.GroupId);
        // Незатрагиваемые мутаторами поля — дословно исходные.
        Assert.Equal("s1", stored.Login);
        Assert.Equal(UserRoles.Student, stored.Role);
        Assert.Equal(student.Id, users.GetByEmail($"s1-{rounds - 1:000}@example.com")!.Id);
        Assert.Null(users.GetByEmail("s1@example.com"));
    }

    [Fact]
    public async Task EightThreads_Times200Rounds_NarrowMutatorsWithReaders_NoLostUpdates()
    {
        // AC T-302 «Взаимная невытираемость мутаций» (профиль NFR-005, 8×200):
        // все 8 потоков — 3 мутатора ОДНОЙ записи (SetPassword × UpdateProfile ×
        // SetGroup) и 5 читателей — проходят 200 барьерных раундов смешанных
        // операций. После финального раунда каждое мутируемое поле хранит
        // значение последнего раунда — полная замена устаревшим снимком откатила
        // бы соседние поля к более раннему раунду; читатели не наблюдают
        // исключений и «чужих» значений немутируемых полей.
        const int rounds = 200;
        const int readers = 5;
        var users = new InMemoryUserRepository(new StorageLock());
        var groupA = TestEntities.Group("ИК-221");
        var student = TestEntities.User("s1", "s1@example.com");
        student.PasswordHash = "hash-initial";
        users.Add(student);

        using var barrier = new Barrier(3 + readers);
        var failures = new ConcurrentQueue<Exception>();

        // Сбой внутри раунда (Assert/мутатор) не прерывает участие в барьере —
        // иначе остальные потоки зависли бы на SignalAndWait; факт сбоя
        // финализируется после прогона.
        var setPassword = Task.Run(() =>
        {
            for (var i = 0; i < rounds; i++)
            {
                try
                {
                    if (!users.SetPassword(student.Id, $"hash-{i:000}"))
                    {
                        failures.Enqueue(new InvalidOperationException("SetPassword вернул false."));
                    }
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }

                barrier.SignalAndWait();
            }
        });
        var updateProfile = Task.Run(() =>
        {
            for (var i = 0; i < rounds; i++)
            {
                try
                {
                    if (users.UpdateProfile(student.Id, $"ФИО-{i:000}", $"s1-{i:000}@example.com")
                        != UpdateProfileResult.Success)
                    {
                        failures.Enqueue(new InvalidOperationException("UpdateProfile не Success."));
                    }
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }

                barrier.SignalAndWait();
            }
        });
        var setGroup = Task.Run(() =>
        {
            for (var i = 0; i < rounds; i++)
            {
                try
                {
                    if (users.SetGroup(student.Id, groupA.Id, _ => true) != SetGroupResult.Success)
                    {
                        failures.Enqueue(new InvalidOperationException("SetGroup не Success."));
                    }
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }

                barrier.SignalAndWait();
            }
        });

        // Читатели (5 из 8 потоков — смешанная нагрузка NFR-005): снимок атомарен,
        // поэтому Login/Role снимка всегда исходные, запись всегда разрешается по
        // неизменяемому login, а email снимка либо уже перепривязан дальше (null),
        // либо указывает на ту же запись — но никогда на чужую.
        var readerTasks = Enumerable.Range(0, readers)
            .Select(readerIndex => Task.Run(() =>
            {
                for (var i = 0; i < rounds; i++)
                {
                    try
                    {
                        var snapshot = users.GetById(student.Id)
                            ?? throw new InvalidOperationException("Запись исчезла под нагрузкой.");
                        Assert.Equal("s1", snapshot.Login);
                        Assert.Equal(UserRoles.Student, snapshot.Role);
                        Assert.Equal(student.Id, users.GetByLogin("S1")!.Id);
                        Assert.Equal(student.Id, users.GetByLogin(snapshot.Login)!.Id);

                        var byEmail = users.GetByEmail(snapshot.Email);
                        Assert.True(byEmail is null || byEmail.Id == student.Id);

                        _ = users.ListStudents();
                        _ = users.ListByGroup(groupA.Id);
                        _ = users.CountByGroup(groupA.Id);
                    }
                    catch (Exception ex)
                    {
                        failures.Enqueue(ex);
                    }

                    barrier.SignalAndWait();
                }
            }))
            .ToArray();

        var allTasks = new List<Task> { setPassword, updateProfile, setGroup };
        allTasks.AddRange(readerTasks);
        await Task.WhenAll(allTasks);
        Assert.Empty(failures);

        // Гейт CR-001: ни одна из серий мутаций не потеряна — каждое поле хранит
        // значение ПОСЛЕДНЕГО раунда своего мутатора.
        var stored = users.GetById(student.Id)!;
        Assert.Equal($"hash-{rounds - 1:000}", stored.PasswordHash);
        Assert.Equal($"ФИО-{rounds - 1:000}", stored.FullName);
        Assert.Equal($"s1-{rounds - 1:000}@example.com", stored.Email);
        Assert.Equal(groupA.Id, stored.GroupId);
        Assert.Equal("s1", stored.Login);
        Assert.Equal(UserRoles.Student, stored.Role);
        Assert.Equal(student.Id, users.GetByEmail($"s1-{rounds - 1:000}@example.com")!.Id);
        Assert.Null(users.GetByEmail("s1@example.com"));
    }

    [Fact]
    public async Task ParallelUpdateProfiles_SameEmail_RoundsExactlyOneWinnerPerRound()
    {
        // IF-015 EMAIL_CONFLICT: проверка занятости и запись — одна критическая
        // секция. Барьерная гонка: в каждом раунде оба пользователя одновременно
        // претендуют на СВОБОДНЫЙ email — ровно один Success, второй EmailConflict.
        const int rounds = 200;
        var users = new InMemoryUserRepository(new StorageLock());
        var first = TestEntities.User("a", "a@example.com");
        var second = TestEntities.User("b", "b@example.com");
        users.Add(first);
        users.Add(second);
        using var barrier = new Barrier(2);
        var failures = new ConcurrentQueue<Exception>();
        var results = new UpdateProfileResult[rounds * 2];

        var run = (int slot, Guid userId) => Task.Run(() =>
        {
            for (var i = 0; i < rounds; i++)
            {
                try
                {
                    results[(i * 2) + slot] = users.UpdateProfile(
                        userId, "Обновление", $"shared{i:000}@example.com");
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }

                barrier.SignalAndWait();
            }
        });

        await Task.WhenAll(run(0, first.Id), run(1, second.Id));
        Assert.Empty(failures);

        for (var i = 0; i < rounds; i++)
        {
            var (firstResult, secondResult) = (results[i * 2], results[(i * 2) + 1]);
            Assert.True(
                (firstResult == UpdateProfileResult.Success)
                    != (secondResult == UpdateProfileResult.Success),
                $"Раунд {i}: ожидался ровно один Success, фактически {firstResult}/{secondResult}.");
            Assert.All(
                new[] { firstResult, secondResult },
                result => Assert.NotEqual(UpdateProfileResult.UserNotFound, result));
        }

        // Итог: email последнего раунда принадлежит ровно одному из участников
        // (перебинтовка byEmail: оба исходных email освобождены).
        var lastOwner = users.GetByEmail($"shared{rounds - 1:000}@example.com");
        Assert.NotNull(lastOwner);
        Assert.True(
            lastOwner!.Id == first.Id || lastOwner.Id == second.Id,
            "Владелец финального email — один из двух участников.");
        Assert.Equal(2, users.ListStudents().Count);
    }

    // ------------------------------------------------------------------
    // Потокобезопасность (AC FR-024): ≥100 параллельных созданий из
    // нескольких потоков — все записи сохранены, исключений нет.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ParallelAdds_FromMultipleThreads_AllSaved_NoExceptions()
    {
        const int threads = 8;
        const int perThread = 25;
        var users = new InMemoryUserRepository(new StorageLock());
        using var startGate = new Barrier(threads);

        var tasks = Enumerable.Range(0, threads)
            .Select(threadIndex => Task.Run(() =>
            {
                startGate.SignalAndWait();
                for (var i = 1; i <= perThread; i++)
                {
                    var nn = threadIndex * perThread + i;
                    users.Add(TestEntities.User($"user{nn:000}", $"user{nn:000}@example.com"));
                }
            }))
            .ToArray();

        // Исключение любого потока провалит WhenAll и тест.
        await Task.WhenAll(tasks);

        Assert.Equal(threads * perThread, users.ListStudents().Count);
    }

    [Fact]
    public async Task ParallelAdds_SameLogin_ExactlyOneWinner()
    {
        const int contenders = 100;
        var users = new InMemoryUserRepository(new StorageLock());
        using var startGate = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, contenders)
            .Select(_ => Task.Run(() =>
            {
                startGate.Wait();
                try
                {
                    users.Add(TestEntities.User("same-login", "same-login@example.com"));
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
    }

    [Fact]
    public async Task ParallelMixedOperations_NoDamage()
    {
        var users = new InMemoryUserRepository(new StorageLock());
        users.Add(TestEntities.User("seeded"));

        var tasks = Enumerable.Range(0, 4)
            .Select(threadIndex => Task.Run(() =>
            {
                for (var i = 0; i < 25; i++)
                {
                    var nn = threadIndex * 25 + i;
                    users.Add(TestEntities.User($"user{nn:000}", $"user{nn:000}@example.com"));
                    _ = users.GetByLogin($"user{nn:000}");
                    _ = users.ListStudents();
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(101, users.ListStudents().Count);
    }
}
