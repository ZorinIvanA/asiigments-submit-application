using System.Diagnostics.Metrics;
using System.Globalization;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.Tests.Storage;

/// <summary>
/// Юнит-проверки SeedRunner (FR-025): все ветки Seed__DemoData (умолчание
/// Development, явные true/false, некорректное значение; в Production — всегда
/// false, явное true игнорируется), состав демо-набора
/// (1 преподаватель + 32 студента + 3 группы + 23 работы + 4 сдачи),
/// кастомные Seed__Teacher*, «§8 к сид-паролю не применяется», идемпотентность
/// по каждому типу естественного ключа (login/email/имя группы/(semester,number)/
/// (student,lab)) и гейт Δkdf{seed} = 33 (IF-002: метка seed, T-005).
/// Пароли сида хэшируются IPasswordHasher (Pbkdf2PasswordHasher, малые итерации —
/// FR-027); проверки паролей — Verify того же хэшера.
/// </summary>
public sealed class SeedRunnerTests
{
    /// <summary>Тестовые итерации PBKDF2 (FR-027: малые итерации в тестах).</summary>
    private const int TestIterations = 1000;

    [Fact]
    public void Development_NoDemoDataVariable_SeedsFullDemoSet()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);

        harness.Runner.Run();

        AssertTeacher(storage, harness.Hasher);
        AssertDemoSet(storage);
    }

    [Fact]
    public void Production_NoDemoDataVariable_SeedsTeacherOnly()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Production", demoData: null);

        harness.Runner.Run();

        AssertTeacher(storage, harness.Hasher);
        Assert.Empty(storage.Users.ListStudents());
        Assert.Empty(storage.Groups.GetAll());
        Assert.Empty(storage.Labs.GetAll());
    }

    // FR-025: вне Development флаг ВСЕГДА трактуется как false —
    // явное 'true' игнорируется (opt-in в Production удалён).
    [Fact]
    public void Production_ExplicitTrue_Ignored_SeedsTeacherOnly()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Production", demoData: "TRUE");

        harness.Runner.Run();

        AssertTeacher(storage, harness.Hasher);
        Assert.Empty(storage.Users.ListStudents());
        Assert.Empty(storage.Groups.GetAll());
        Assert.Empty(storage.Labs.GetAll());
    }

    [Fact]
    public void Development_ExplicitFalse_SeedsTeacherOnly()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: "false");

        harness.Runner.Run();

        AssertTeacher(storage, harness.Hasher);
        Assert.Empty(storage.Users.ListStudents());
        Assert.Empty(storage.Groups.GetAll());
        Assert.Empty(storage.Labs.GetAll());
    }

    // ------------------------------------------------------------------
    // AC FR-025 «Некорректное Seed__DemoData»: "yes" трактуется как умолчание
    // окружения; предупреждение пишет слой конфигурации (проверяется в
    // SeedRunnerHostTests), сид продолжает работать.
    // ------------------------------------------------------------------

    [Fact]
    public void Development_UnknownValue_UsesEnvironmentDefault()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: "yes");

        harness.Runner.Run();

        AssertTeacher(storage, harness.Hasher);
        AssertDemoSet(storage);
    }

    [Fact]
    public void Production_UnknownValue_UsesEnvironmentDefault_TeacherOnly()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Production", demoData: "2");

        harness.Runner.Run();

        AssertTeacher(storage, harness.Hasher);
        Assert.Empty(storage.Users.ListStudents());
    }

    // ------------------------------------------------------------------
    // Гейт Δkdf{seed} (IF-002, AC T-005): «каждая деривация считается» —
    // чистое хранилище с демо-набором даёт ровно 33 деривации с меткой seed
    // (32 студента + преподаватель); повторный запуск не добавляет ни одной.
    // ------------------------------------------------------------------

    [Fact]
    public void Run_WithDemoData_SeedKdfDelta_Is33()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        var before = harness.Counter.Snapshot();

        harness.Runner.Run();

        Assert.Equal(33, Delta(harness.Counter, before, KdfCallers.Seed));
    }

    [Fact]
    public void Run_TeacherOnly_SeedKdfDelta_Is1()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: "false");
        var before = harness.Counter.Snapshot();

        harness.Runner.Run();

        Assert.Equal(1, Delta(harness.Counter, before, KdfCallers.Seed));
    }

    [Fact]
    public void Run_Twice_NoAdditionalSeedDerivations()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);

        harness.Runner.Run();
        var afterFirst = harness.Counter.Snapshot();
        harness.Runner.Run();

        Assert.Equal(0, Delta(harness.Counter, afterFirst, KdfCallers.Seed));
        AssertTeacher(storage, harness.Hasher);
        AssertDemoSet(storage);
    }

    // ------------------------------------------------------------------
    // Формат сид-хэша (IF-002, T-105): пароли сид-учёток хранятся строкой
    // «pbkdf2-sha256$<iterations>$<saltBase64>$<hashBase64>» — ровно 4
    // сегмента, разбираемых Pbkdf2PasswordHasher. Удалённый SeedPasswordHasher
    // писал ведущий '$' (пустой первый сегмент — 5 сегментов): все сид-хэши
    // классифицировались malformed → Verify=false без деривации → вход
    // сид-учёток давал 401 (корень BUG-001..004).
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("teacher", "teacher123!")]
    [InlineData("student01", "student123!")]
    [InlineData("student32", "student123!")]
    public void Run_SeededPasswordHash_HasIf002FourSegmentFormat(string login, string password)
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);

        harness.Runner.Run();

        var hash = storage.Users.GetByLogin(login)!.PasswordHash;
        Assert.StartsWith(Pbkdf2PasswordHasher.AlgorithmMarker + "$", hash, StringComparison.Ordinal);

        // Ровно 4 сегмента без пустого первого: маркер, итерации, соль, ключ.
        var segments = hash.Split('$');
        Assert.Equal(4, segments.Length);
        Assert.Equal(TestIterations.ToString(CultureInfo.InvariantCulture), segments[1]);
        Assert.Equal(Pbkdf2PasswordHasher.SaltSizeBytes, Convert.FromBase64String(segments[2]).Length);
        Assert.Equal(Pbkdf2PasswordHasher.HashSizeBytes, Convert.FromBase64String(segments[3]).Length);
        Assert.True(hash.Length <= 500);

        // Разбираемость формата — поведенчески: Verify читает параметры из
        // самого хэша (внутри — Pbkdf2PasswordHasher.TryParse) и проходит
        // ровно по сид-паролю.
        Assert.True(harness.Hasher.Verify(password, hash, KdfCallers.Login));
    }

    // ------------------------------------------------------------------
    // Идемпотентность (AC FR-025): повторный вызов сида не меняет число
    // пользователей/групп/работ/сдач.
    // ------------------------------------------------------------------

    [Fact]
    public void Run_Twice_Idempotent()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);

        harness.Runner.Run();
        harness.Runner.Run();

        AssertTeacher(storage, harness.Hasher);
        AssertDemoSet(storage);
    }

    // Идемпотентность по естественному ключу lower(login): существующий студент
    // не дублируется и не перезаписывается (пароль/ email остаются его собственными).
    [Fact]
    public void Run_ExistingStudentLogin_NotDuplicated_NotOverwritten()
    {
        var storage = TestStorage.Create();
        var existing = new User
        {
            Id = Guid.NewGuid(),
            Login = "student05",
            Email = "ghost05@example.com",
            PasswordHash = "marker-hash",
            FullName = "Существующий Студент Студентович",
            Role = UserRoles.Student,
            GroupId = null,
            CreatedAt = DateTime.UtcNow,
        };
        storage.Users.Add(existing);

        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        harness.Runner.Run();

        Assert.Equal(32, storage.Users.ListStudents().Count); // 31 новых + 1 существующий
        var stored = storage.Users.GetByLogin("STUDENT05")!;   // ci-поиск
        Assert.Equal(existing.Id, stored.Id);
        Assert.Equal("marker-hash", stored.PasswordHash);
        Assert.Equal("ghost05@example.com", stored.Email);
        Assert.Null(stored.GroupId);
        Assert.True(harness.Hasher.Verify("student123!", storage.Users.GetByLogin("student06")!.PasswordHash, KdfCallers.Login));
    }

    // Идемпотентность по естественному ключу lower(email): занятый сид-email
    // не приводит к конфликту уникальности — студент пропускается.
    [Fact]
    public void Run_ExistingStudentEmail_NotDuplicated_NotOverwritten()
    {
        var storage = TestStorage.Create();
        var existing = new User
        {
            Id = Guid.NewGuid(),
            Login = "ghost",
            Email = "student08@example.com",
            PasswordHash = "marker-hash",
            FullName = "Существующий Студент Студентович",
            Role = UserRoles.Student,
            GroupId = null,
            CreatedAt = DateTime.UtcNow,
        };
        storage.Users.Add(existing);

        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        harness.Runner.Run();

        Assert.Null(storage.Users.GetByLogin("student08")); // студент пропущен
        Assert.Equal(32, storage.Users.ListStudents().Count); // 31 новых + ghost
        var stored = storage.Users.GetByLogin("ghost")!;
        Assert.Equal(existing.Id, stored.Id);
        Assert.Equal("marker-hash", stored.PasswordHash);
    }

    // Идемпотентность по естественному ключу — ci-имя группы: существующая группа
    // переиспользуется (студенты 01–25 привязываются к ней), дублей нет.
    [Fact]
    public void Run_ExistingGroupNameCi_ReusedForStudents()
    {
        var storage = TestStorage.Create();
        var existing = TestEntities.Group("ик-221"); // ci-равно «ИК-221»
        storage.Groups.Add(existing);

        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        harness.Runner.Run();

        Assert.Equal(3, storage.Groups.GetAll().Count);
        var student01 = storage.Users.GetByLogin("student01")!;
        Assert.Equal(existing.Id, student01.GroupId);
        var student25 = storage.Users.GetByLogin("student25")!;
        Assert.Equal(existing.Id, student25.GroupId);
    }

    // Идемпотентность по естественному ключу (semester, number): существующая
    // работа не дублируется и не перезаписывается.
    [Fact]
    public void Run_ExistingLabPair_NotDuplicated_NotOverwritten()
    {
        var storage = TestStorage.Create();
        var existing = TestEntities.Lab(1, 1);
        existing.Content = "пользовательская работа";
        existing.AssignmentUrl = "https://custom.example.com/lab";
        storage.Labs.Add(existing);

        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        harness.Runner.Run();

        Assert.Equal(23, storage.Labs.GetAll().Count);
        var stored = storage.Labs.TryGetByPair(1, 1)!;
        Assert.Equal(existing.Id, stored.Id);
        Assert.Equal("пользовательская работа", stored.Content);
        Assert.Equal("https://custom.example.com/lab", stored.AssignmentUrl);
    }

    // Идемпотентность по естественному ключу (student, lab): повторный сид
    // обновляет сдачу по правилам seed.ts на месте, не создавая дублей.
    [Fact]
    public void Run_ExistingSubmissionPair_UpdatedInPlaceNotDuplicated()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        harness.Runner.Run();

        var student01 = storage.Users.GetByLogin("student01")!;
        var lab11 = storage.Labs.TryGetByPair(1, 1)!;
        storage.Submissions.Upsert(
            student01.Id, lab11.Id, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2), updatedBy: null, DateTime.UtcNow);

        harness.Runner.Run();

        var allLabIds = storage.Labs.GetAll().Select(lab => lab.Id).ToList();
        Assert.Equal(4, storage.Submissions.ListByLabIds(allLabIds).Count);
        var stored = storage.Submissions.GetByStudentAndLab(student01.Id, lab11.Id)!;
        Assert.Equal(new DateOnly(2026, 9, 1), stored.SubmitDate);
        Assert.Equal(new DateOnly(2026, 9, 11), stored.DefenseDate);
        Assert.Equal(storage.Users.GetByLogin("teacher")!.Id, stored.UpdatedBy);
    }

    // Пер-ключевая идемпотентность (T-005): сид-преподаватель уже существует
    // (например, прошлый запуск без демо-данных) — демо-набор всё равно создаётся,
    // а существующая учётка не перехэшируется (Δkdf{seed} = 32, без преподавателя).
    [Fact]
    public void Run_AfterTeacherOnlyRun_SeedsDemoDataWithoutRehashingTeacher()
    {
        var storage = TestStorage.Create();
        var first = CreateHarness(storage, environment: "Development", demoData: "false");
        first.Runner.Run();

        var second = CreateHarness(storage, environment: "Development", demoData: null);
        var before = second.Counter.Snapshot();
        second.Runner.Run();

        Assert.Equal(32, Delta(second.Counter, before, KdfCallers.Seed));
        AssertTeacher(storage, second.Hasher);
        AssertDemoSet(storage);
    }

    // ------------------------------------------------------------------
    // Кастомные Seed__Teacher* и §8.
    // ------------------------------------------------------------------

    [Fact]
    public void CustomTeacherLoginAndPassword_SeededAndVerifiable()
    {
        var storage = TestStorage.Create();
        var options = new SeedOptions { TeacherLogin = "admin", TeacherPassword = "s3cret-Br4nd!" };

        var harness = CreateHarness(storage, environment: "Development", options: options);
        harness.Runner.Run();

        var teacher = storage.Users.GetByLogin("ADMIN"); // ci-поиск
        Assert.NotNull(teacher);
        Assert.Equal("admin", teacher.Login);
        Assert.Equal(UserRoles.Teacher, teacher.Role);
        Assert.Null(teacher.GroupId);
        Assert.Equal("admin@example.com", teacher.Email);
        Assert.True(harness.Hasher.Verify("s3cret-Br4nd!", teacher.PasswordHash, KdfCallers.Login));
    }

    // §8 к сид-паролю НЕ применяется (FR-025): короткий/слабый пароль сида валиден.
    [Theory]
    [InlineData("1")]
    [InlineData("a")]
    public void SeedPassword_ShortOrWeak_NotValidatedBySection8(string seedPassword)
    {
        var storage = TestStorage.Create();
        var options = new SeedOptions { TeacherPassword = seedPassword };

        var harness = CreateHarness(storage, environment: "Development", options: options);
        harness.Runner.Run();

        var teacher = storage.Users.GetByLogin("teacher");
        Assert.NotNull(teacher);
        Assert.True(harness.Hasher.Verify(seedPassword, teacher.PasswordHash, KdfCallers.Login));
    }

    // SEC-001 (регресс): пароль сида не триммится — guard (SeedOptionsValidator) и
    // сид работают с ОДНОЙ строкой. Значение по умолчанию, обрамлённое пробелами,
    // хэшируется дословно, а не «схлопывается» тримом к документированному дефолту
    // (иначе учётная запись получала бы пароль, который guard не проверял).
    [Fact]
    public void SeedPassword_WithLeadingTrailingSpaces_HashedVerbatimNotTrimmed()
    {
        var storage = TestStorage.Create();
        var options = new SeedOptions { TeacherPassword = " teacher123! " };

        var harness = CreateHarness(storage, environment: "Development", options: options);
        harness.Runner.Run();

        var teacher = storage.Users.GetByLogin("teacher")!;
        Assert.True(harness.Hasher.Verify(" teacher123! ", teacher.PasswordHash, KdfCallers.Login));
        Assert.False(harness.Hasher.Verify("teacher123!", teacher.PasswordHash, KdfCallers.Login));
    }

    // SEC-001 (регресс): пробелами нельзя «спрятать» слабое значение — эффективный
    // пароль учётной записи равен сырому значению переменной, а не обрезанному.
    [Fact]
    public void SeedPassword_PaddedWeakValue_EffectivePasswordIsRawValue()
    {
        var storage = TestStorage.Create();
        var options = new SeedOptions { TeacherPassword = "   ab1!   " };

        var harness = CreateHarness(storage, environment: "Development", options: options);
        harness.Runner.Run();

        var teacher = storage.Users.GetByLogin("teacher")!;
        Assert.True(harness.Hasher.Verify("   ab1!   ", teacher.PasswordHash, KdfCallers.Login));
        Assert.False(harness.Hasher.Verify("ab1!", teacher.PasswordHash, KdfCallers.Login));
    }

    // Пустое/пробельное значение (возможно только вне Production — guard FR-025
    // отклоняет его раньше) → умолчание «teacher123!».
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SeedPassword_MissingOrWhitespace_FallsBackToDefault(string? seedPassword)
    {
        var storage = TestStorage.Create();
        var options = new SeedOptions { TeacherPassword = seedPassword! };

        var harness = CreateHarness(storage, environment: "Development", options: options);
        harness.Runner.Run();

        AssertTeacher(storage, harness.Hasher);
    }

    [Fact]
    public void EmptyTeacherLogin_FallsBackToDefault()
    {
        var storage = TestStorage.Create();
        var options = new SeedOptions { TeacherLogin = "   " };

        var harness = CreateHarness(storage, environment: "Development", options: options);
        harness.Runner.Run();

        Assert.NotNull(storage.Users.GetByLogin("teacher"));
    }

    [Fact]
    public void DemoSet_StudentPassword_Verifies()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        harness.Runner.Run();

        var student01 = storage.Users.GetByLogin("student01")!;
        Assert.True(harness.Hasher.Verify("student123!", student01.PasswordHash, KdfCallers.Login));
    }

    // ------------------------------------------------------------------
    // Состав демо-набора (детальные проверки).
    // ------------------------------------------------------------------

    [Fact]
    public void DemoSet_GroupsAndStudents()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        harness.Runner.Run();

        var ik221 = storage.Groups.GetByName("ИК-221");
        var ik222 = storage.Groups.GetByName("ИК-222");
        var ik223 = storage.Groups.GetByName("ИК-223");
        Assert.NotNull(ik221);
        Assert.NotNull(ik222);
        Assert.NotNull(ik223);
        Assert.Equal(3, storage.Groups.GetAll().Count);

        Assert.Equal(32, storage.Users.ListStudents().Count);
        Assert.Null(storage.Users.GetByLogin("Student33"));

        var student01 = storage.Users.GetByLogin("student01")!;
        var student25 = storage.Users.GetByLogin("student25")!;
        var student26 = storage.Users.GetByLogin("student26")!;
        var student30 = storage.Users.GetByLogin("student30")!;
        var student31 = storage.Users.GetByLogin("student31")!;
        var student32 = storage.Users.GetByLogin("student32")!;
        Assert.Equal(ik221!.Id, student01.GroupId);
        Assert.Equal(ik221.Id, student25.GroupId);
        Assert.Equal(ik222!.Id, student26.GroupId);
        Assert.Equal(ik222.Id, student30.GroupId);
        Assert.Null(student31.GroupId);
        Assert.Null(student32.GroupId);

        Assert.Equal("student01@example.com", student01.Email);
        Assert.Equal("Иванов Иван Иванович 01", student01.FullName);
        Assert.Equal("student32@example.com", storage.Users.GetByLogin("student32")!.Email);
        Assert.Equal("Иванов Иван Иванович 32", storage.Users.GetByLogin("student32")!.FullName);
    }

    [Fact]
    public void DemoSet_Labs_CompositionAndAttributes()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        harness.Runner.Run();

        // 20 работ семестра 1 + 3 работы семестра 2; за границами — нет.
        Assert.Equal(23, storage.Labs.GetAll().Count);
        Assert.NotNull(storage.Labs.TryGetByPair(1, 1));
        Assert.NotNull(storage.Labs.TryGetByPair(1, 20));
        Assert.Null(storage.Labs.TryGetByPair(1, 21));
        Assert.NotNull(storage.Labs.TryGetByPair(2, 3));
        Assert.Null(storage.Labs.TryGetByPair(2, 4));

        // Содержание, защита у чётных, ссылка у кратных 5.
        var lab11 = storage.Labs.TryGetByPair(1, 1)!;
        Assert.Equal("Содержание лабораторной работы №1", lab11.Content);
        Assert.False(lab11.DefenseRequired);
        Assert.Null(lab11.AssignmentUrl);

        var lab12 = storage.Labs.TryGetByPair(1, 2)!;
        Assert.True(lab12.DefenseRequired);
        Assert.Null(lab12.AssignmentUrl);

        var lab15 = storage.Labs.TryGetByPair(1, 5)!;
        Assert.False(lab15.DefenseRequired);
        Assert.Equal("https://git.example.com/assignments/1/5", lab15.AssignmentUrl);

        var lab110 = storage.Labs.TryGetByPair(1, 10)!;
        Assert.True(lab110.DefenseRequired);
        Assert.Equal("https://git.example.com/assignments/1/10", lab110.AssignmentUrl);

        var lab21 = storage.Labs.TryGetByPair(2, 1)!;
        Assert.Equal("Содержание лабораторной работы №1", lab21.Content);
        Assert.Null(lab21.AssignmentUrl);
    }

    [Fact]
    public void DemoSet_Submissions_ExactRulesWithTeacherStamp()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        harness.Runner.Run();

        var teacher = storage.Users.GetByLogin("teacher")!;
        var student01 = storage.Users.GetByLogin("student01")!;
        var student02 = storage.Users.GetByLogin("student02")!;
        var expectedUpdatedAt = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

        var first = storage.Submissions.GetByStudentAndLab(student01.Id, storage.Labs.TryGetByPair(1, 1)!.Id)!;
        Assert.Equal(new DateOnly(2026, 9, 1), first.SubmitDate);
        Assert.Equal(new DateOnly(2026, 9, 11), first.DefenseDate);
        Assert.Equal(teacher.Id, first.UpdatedBy);
        Assert.Equal(expectedUpdatedAt, first.UpdatedAt);

        var second = storage.Submissions.GetByStudentAndLab(student01.Id, storage.Labs.TryGetByPair(1, 2)!.Id)!;
        Assert.Equal(new DateOnly(2026, 9, 2), second.SubmitDate);
        Assert.Equal(new DateOnly(2026, 9, 12), second.DefenseDate);
        Assert.Equal(teacher.Id, second.UpdatedBy);

        var third = storage.Submissions.GetByStudentAndLab(student01.Id, storage.Labs.TryGetByPair(1, 3)!.Id)!;
        Assert.Equal(new DateOnly(2026, 9, 3), third.SubmitDate);
        Assert.Null(third.DefenseDate);
        Assert.Equal(teacher.Id, third.UpdatedBy);

        var fourth = storage.Submissions.GetByStudentAndLab(student02.Id, storage.Labs.TryGetByPair(1, 1)!.Id)!;
        Assert.Equal(new DateOnly(2026, 9, 1), fourth.SubmitDate);
        Assert.Null(fourth.DefenseDate);
        Assert.Equal(teacher.Id, fourth.UpdatedBy);

        // Ровно 4 сид-сдачи.
        var allLabIds = storage.Labs.GetAll().Select(lab => lab.Id).ToList();
        Assert.Equal(4, storage.Submissions.ListByLabIds(allLabIds).Count);

        // У других студентов сдач нет.
        Assert.Empty(storage.Submissions.ListByStudent(storage.Users.GetByLogin("student03")!.Id));
    }

    // ------------------------------------------------------------------
    // Хелперы.
    // ------------------------------------------------------------------

    /// <summary>Сид + реальные IPasswordHasher/IKdfCounter (гейт Δkdf{seed}, IF-002).</summary>
    private sealed record SeedHarness(
        SeedRunner Runner,
        Pbkdf2PasswordHasher Hasher,
        KdfCounter Counter)
    {
        public void Run() => Runner.Run();
    }

    private static long Delta(IKdfCounter counter, IReadOnlyDictionary<string, long> before, string caller) =>
        counter.Snapshot().GetValueOrDefault(caller) - before.GetValueOrDefault(caller);

    private static void AssertTeacher(TestStorage storage, Pbkdf2PasswordHasher hasher)
    {
        var teacher = storage.Users.GetByLogin("teacher");
        Assert.NotNull(teacher);
        Assert.Equal(UserRoles.Teacher, teacher.Role);
        Assert.Null(teacher.GroupId);
        Assert.Equal("teacher@example.com", teacher.Email);
        Assert.True(hasher.Verify("teacher123!", teacher.PasswordHash, KdfCallers.Login));
    }

    private static void AssertDemoSet(TestStorage storage)
    {
        Assert.Equal(32, storage.Users.ListStudents().Count);
        Assert.Equal(3, storage.Groups.GetAll().Count);
        Assert.Equal(23, storage.Labs.GetAll().Count);
        var allLabIds = storage.Labs.GetAll().Select(lab => lab.Id).ToList();
        Assert.Equal(4, storage.Submissions.ListByLabIds(allLabIds).Count);
    }

    private static SeedHarness CreateHarness(
        TestStorage storage,
        string environment,
        string? demoData = null,
        SeedOptions? options = null)
    {
        var counter = CreateCounter();
        var hasher = new Pbkdf2PasswordHasher(
            Options.Create(new AuthOptions { Pbkdf2Iterations = TestIterations }), counter);
        var seedOptions = options ?? new SeedOptions { DemoData = demoData };
        var runner = new SeedRunner(
            storage.Users,
            storage.Groups,
            storage.Labs,
            storage.Submissions,
            hasher,
            Options.Create(seedOptions),
            new FakeHostEnvironment(environment),
            NullLogger<SeedRunner>.Instance);
        return new SeedHarness(runner, hasher, counter);
    }

    private static KdfCounter CreateCounter()
    {
        var meter = new Meter("LabsApp.Tests.Storage.SeedRunner", "1.0");
        return new KdfCounter(meter, new FakeTimeProvider());
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "LabsApp.Tests";

        public string ContentRootPath { get; set; } = "/";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
