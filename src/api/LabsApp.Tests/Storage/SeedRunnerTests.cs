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
    public async Task Development_NoDemoDataVariable_SeedsFullDemoSet()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);

        await harness.Runner.RunAsync();

        await AssertTeacherAsync(storage, harness.Hasher);
        await AssertDemoSetAsync(storage);
    }

    [Fact]
    public async Task Production_NoDemoDataVariable_SeedsTeacherOnly()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Production", demoData: null);

        await harness.Runner.RunAsync();

        await AssertTeacherAsync(storage, harness.Hasher);
        Assert.Empty(await storage.Users.ListStudentsAsync());
        Assert.Empty(await storage.Groups.GetAllAsync());
        Assert.Empty(await storage.Labs.GetAllAsync());
    }

    // FR-025: вне Development флаг ВСЕГДА трактуется как false —
    // явное 'true' игнорируется (opt-in в Production удалён).
    [Fact]
    public async Task Production_ExplicitTrue_Ignored_SeedsTeacherOnly()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Production", demoData: "TRUE");

        await harness.Runner.RunAsync();

        await AssertTeacherAsync(storage, harness.Hasher);
        Assert.Empty(await storage.Users.ListStudentsAsync());
        Assert.Empty(await storage.Groups.GetAllAsync());
        Assert.Empty(await storage.Labs.GetAllAsync());
    }

    [Fact]
    public async Task Development_ExplicitFalse_SeedsTeacherOnly()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: "false");

        await harness.Runner.RunAsync();

        await AssertTeacherAsync(storage, harness.Hasher);
        Assert.Empty(await storage.Users.ListStudentsAsync());
        Assert.Empty(await storage.Groups.GetAllAsync());
        Assert.Empty(await storage.Labs.GetAllAsync());
    }

    // ------------------------------------------------------------------
    // AC FR-025 «Некорректное Seed__DemoData»: "yes" трактуется как умолчание
    // окружения; предупреждение пишет слой конфигурации (проверяется в
    // SeedRunnerHostTests), сид продолжает работать.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Development_UnknownValue_UsesEnvironmentDefault()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: "yes");

        await harness.Runner.RunAsync();

        await AssertTeacherAsync(storage, harness.Hasher);
        await AssertDemoSetAsync(storage);
    }

    [Fact]
    public async Task Production_UnknownValue_UsesEnvironmentDefault_TeacherOnly()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Production", demoData: "2");

        await harness.Runner.RunAsync();

        await AssertTeacherAsync(storage, harness.Hasher);
        Assert.Empty(await storage.Users.ListStudentsAsync());
    }

    // ------------------------------------------------------------------
    // Гейт Δkdf{seed} (IF-002, AC T-005): «каждая деривация считается» —
    // чистое хранилище с демо-набором даёт ровно 33 деривации с меткой seed
    // (32 студента + преподаватель); повторный запуск не добавляет ни одной.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Run_WithDemoData_SeedKdfDelta_Is33()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        var before = harness.Counter.Snapshot();

        await harness.Runner.RunAsync();

        Assert.Equal(33, Delta(harness.Counter, before, KdfCallers.Seed));
    }

    [Fact]
    public async Task Run_TeacherOnly_SeedKdfDelta_Is1()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: "false");
        var before = harness.Counter.Snapshot();

        await harness.Runner.RunAsync();

        Assert.Equal(1, Delta(harness.Counter, before, KdfCallers.Seed));
    }

    [Fact]
    public async Task Run_Twice_NoAdditionalSeedDerivations()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);

        await harness.Runner.RunAsync();
        var afterFirst = harness.Counter.Snapshot();
        await harness.Runner.RunAsync();

        Assert.Equal(0, Delta(harness.Counter, afterFirst, KdfCallers.Seed));
        await AssertTeacherAsync(storage, harness.Hasher);
        await AssertDemoSetAsync(storage);
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
    public async Task Run_SeededPasswordHash_HasIf002FourSegmentFormat(string login, string password)
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);

        await harness.Runner.RunAsync();

        var hash = (await storage.Users.GetByLoginAsync(login))!.PasswordHash;
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
    public async Task Run_Twice_Idempotent()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);

        await harness.Runner.RunAsync();
        await harness.Runner.RunAsync();

        await AssertTeacherAsync(storage, harness.Hasher);
        await AssertDemoSetAsync(storage);
    }

    // Идемпотентность по естественному ключу lower(login): существующий студент
    // не дублируется и не перезаписывается (пароль/ email остаются его собственными).
    [Fact]
    public async Task Run_ExistingStudentLogin_NotDuplicated_NotOverwritten()
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
        await storage.Users.AddAsync(existing);

        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        await harness.Runner.RunAsync();

        Assert.Equal(32, (await storage.Users.ListStudentsAsync()).Count); // 31 новых + 1 существующий
        var stored = (await storage.Users.GetByLoginAsync("STUDENT05"))!;   // ci-поиск
        Assert.Equal(existing.Id, stored.Id);
        Assert.Equal("marker-hash", stored.PasswordHash);
        Assert.Equal("ghost05@example.com", stored.Email);
        Assert.Null(stored.GroupId);
        Assert.True(harness.Hasher.Verify("student123!", (await storage.Users.GetByLoginAsync("student06"))!.PasswordHash, KdfCallers.Login));
    }

    // Идемпотентность по естественному ключу lower(email): занятый сид-email
    // не приводит к конфликту уникальности — студент пропускается.
    [Fact]
    public async Task Run_ExistingStudentEmail_NotDuplicated_NotOverwritten()
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
        await storage.Users.AddAsync(existing);

        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        await harness.Runner.RunAsync();

        Assert.Null(await await storage.Users.GetByLoginAsync("student08")); // студент пропущен
        Assert.Equal(32, (await storage.Users.ListStudentsAsync()).Count); // 31 новых + ghost
        var stored = (await storage.Users.GetByLoginAsync("ghost"))!;
        Assert.Equal(existing.Id, stored.Id);
        Assert.Equal("marker-hash", stored.PasswordHash);
    }

    // Идемпотентность по естественному ключу — ci-имя группы: существующая группа
    // переиспользуется (студенты 01–25 привязываются к ней), дублей нет.
    [Fact]
    public async Task Run_ExistingGroupNameCi_ReusedForStudents()
    {
        var storage = TestStorage.Create();
        var existing = TestEntities.Group("ик-221"); // ci-равно «ИК-221»
        await storage.Groups.AddAsync(existing);

        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        await harness.Runner.RunAsync();

        Assert.Equal(3, (await storage.Groups.GetAllAsync()).Count);
        var student01 = (await storage.Users.GetByLoginAsync("student01"))!;
        Assert.Equal(existing.Id, student01.GroupId);
        var student25 = (await storage.Users.GetByLoginAsync("student25"))!;
        Assert.Equal(existing.Id, student25.GroupId);
    }

    // Идемпотентность по естественному ключу (semester, number): существующая
    // работа не дублируется и не перезаписывается.
    [Fact]
    public async Task Run_ExistingLabPair_NotDuplicated_NotOverwritten()
    {
        var storage = TestStorage.Create();
        var existing = TestEntities.Lab(1, 1);
        existing.Content = "пользовательская работа";
        existing.AssignmentUrl = "https://custom.example.com/lab";
        await storage.Labs.AddAsync(existing);

        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        await harness.Runner.RunAsync();

        Assert.Equal(23, (await storage.Labs.GetAllAsync()).Count);
        var stored = (await storage.Labs.TryGetByPairAsync(1, 1))!;
        Assert.Equal(existing.Id, stored.Id);
        Assert.Equal("пользовательская работа", stored.Content);
        Assert.Equal("https://custom.example.com/lab", stored.AssignmentUrl);
    }

    // Идемпотентность по естественному ключу (student, lab): повторный сид
    // обновляет сдачу по правилам seed.ts на месте, не создавая дублей.
    [Fact]
    public async Task Run_ExistingSubmissionPair_UpdatedInPlaceNotDuplicated()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        await harness.Runner.RunAsync();

        var student01 = (await storage.Users.GetByLoginAsync("student01"))!;
        var lab11 = (await storage.Labs.TryGetByPairAsync(1, 1))!;
        await storage.Submissions.UpsertAsync(
            student01.Id, lab11.Id, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2), updatedBy: null, DateTime.UtcNow);

        await harness.Runner.RunAsync();

        var allLabIds = (await storage.Labs.GetAllAsync()).Select(lab => lab.Id).ToList();
        Assert.Equal(4, (await storage.Submissions.ListByLabIdsAsync(allLabIds)).Count);
        var stored = (await storage.Submissions.GetByStudentAndLabAsync(student01.Id, lab11.Id))!;
        Assert.Equal(new DateOnly(2026, 9, 1), stored.SubmitDate);
        Assert.Equal(new DateOnly(2026, 9, 11), stored.DefenseDate);
        Assert.Equal((await storage.Users.GetByLoginAsync("teacher"))!.Id, stored.UpdatedBy);
    }

    // Пер-ключевая идемпотентность (T-005): сид-преподаватель уже существует
    // (например, прошлый запуск без демо-данных) — демо-набор всё равно создаётся,
    // а существующая учётка не перехэшируется (Δkdf{seed} = 32, без преподавателя).
    [Fact]
    public async Task Run_AfterTeacherOnlyRun_SeedsDemoDataWithoutRehashingTeacher()
    {
        var storage = TestStorage.Create();
        var first = CreateHarness(storage, environment: "Development", demoData: "false");
        await first.Runner.RunAsync();

        var second = CreateHarness(storage, environment: "Development", demoData: null);
        var before = second.Counter.Snapshot();
        await second.Runner.RunAsync();

        Assert.Equal(32, Delta(second.Counter, before, KdfCallers.Seed));
        await AssertTeacherAsync(storage, second.Hasher);
        await AssertDemoSetAsync(storage);
    }

    // ------------------------------------------------------------------
    // Кастомные Seed__Teacher* и §8.
    // ------------------------------------------------------------------

    [Fact]
    public async Task CustomTeacherLoginAndPassword_SeededAndVerifiable()
    {
        var storage = TestStorage.Create();
        var options = new SeedOptions { TeacherLogin = "admin", TeacherPassword = "s3cret-Br4nd!" };

        var harness = CreateHarness(storage, environment: "Development", options: options);
        await harness.Runner.RunAsync();

        var teacher = await storage.Users.GetByLoginAsync("ADMIN"); // ci-поиск
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
    public async Task SeedPassword_ShortOrWeak_NotValidatedBySection8(string seedPassword)
    {
        var storage = TestStorage.Create();
        var options = new SeedOptions { TeacherPassword = seedPassword };

        var harness = CreateHarness(storage, environment: "Development", options: options);
        await harness.Runner.RunAsync();

        var teacher = await storage.Users.GetByLoginAsync("teacher");
        Assert.NotNull(teacher);
        Assert.True(harness.Hasher.Verify(seedPassword, teacher.PasswordHash, KdfCallers.Login));
    }

    // SEC-001 (регресс): пароль сида не триммится — guard (SeedOptionsValidator) и
    // сид работают с ОДНОЙ строкой. Значение по умолчанию, обрамлённое пробелами,
    // хэшируется дословно, а не «схлопывается» тримом к документированному дефолту
    // (иначе учётная запись получала бы пароль, который guard не проверял).
    [Fact]
    public async Task SeedPassword_WithLeadingTrailingSpaces_HashedVerbatimNotTrimmed()
    {
        var storage = TestStorage.Create();
        var options = new SeedOptions { TeacherPassword = " teacher123! " };

        var harness = CreateHarness(storage, environment: "Development", options: options);
        await harness.Runner.RunAsync();

        var teacher = (await storage.Users.GetByLoginAsync("teacher"))!;
        Assert.True(harness.Hasher.Verify(" teacher123! ", teacher.PasswordHash, KdfCallers.Login));
        Assert.False(harness.Hasher.Verify("teacher123!", teacher.PasswordHash, KdfCallers.Login));
    }

    // SEC-001 (регресс): пробелами нельзя «спрятать» слабое значение — эффективный
    // пароль учётной записи равен сырому значению переменной, а не обрезанному.
    [Fact]
    public async Task SeedPassword_PaddedWeakValue_EffectivePasswordIsRawValue()
    {
        var storage = TestStorage.Create();
        var options = new SeedOptions { TeacherPassword = "   ab1!   " };

        var harness = CreateHarness(storage, environment: "Development", options: options);
        await harness.Runner.RunAsync();

        var teacher = (await storage.Users.GetByLoginAsync("teacher"))!;
        Assert.True(harness.Hasher.Verify("   ab1!   ", teacher.PasswordHash, KdfCallers.Login));
        Assert.False(harness.Hasher.Verify("ab1!", teacher.PasswordHash, KdfCallers.Login));
    }

    // Пустое/пробельное значение (возможно только вне Production — guard FR-025
    // отклоняет его раньше) → умолчание «teacher123!».
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SeedPassword_MissingOrWhitespace_FallsBackToDefault(string? seedPassword)
    {
        var storage = TestStorage.Create();
        var options = new SeedOptions { TeacherPassword = seedPassword! };

        var harness = CreateHarness(storage, environment: "Development", options: options);
        await harness.Runner.RunAsync();

        await AssertTeacherAsync(storage, harness.Hasher);
    }

    [Fact]
    public async Task EmptyTeacherLogin_FallsBackToDefault()
    {
        var storage = TestStorage.Create();
        var options = new SeedOptions { TeacherLogin = "   " };

        var harness = CreateHarness(storage, environment: "Development", options: options);
        await harness.Runner.RunAsync();

        Assert.NotNull(await await storage.Users.GetByLoginAsync("teacher"));
    }

    [Fact]
    public async Task DemoSet_StudentPassword_Verifies()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        await harness.Runner.RunAsync();

        var student01 = (await storage.Users.GetByLoginAsync("student01"))!;
        Assert.True(harness.Hasher.Verify("student123!", student01.PasswordHash, KdfCallers.Login));
    }

    // ------------------------------------------------------------------
    // Состав демо-набора (детальные проверки).
    // ------------------------------------------------------------------

    [Fact]
    public async Task DemoSet_GroupsAndStudents()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        await harness.Runner.RunAsync();

        var ik221 = await storage.Groups.GetByNameAsync("ИК-221");
        var ik222 = await storage.Groups.GetByNameAsync("ИК-222");
        var ik223 = await storage.Groups.GetByNameAsync("ИК-223");
        Assert.NotNull(ik221);
        Assert.NotNull(ik222);
        Assert.NotNull(ik223);
        Assert.Equal(3, (await storage.Groups.GetAllAsync()).Count);

        Assert.Equal(32, (await storage.Users.ListStudentsAsync()).Count);
        Assert.Null(await await storage.Users.GetByLoginAsync("Student33"));

        var student01 = (await storage.Users.GetByLoginAsync("student01"))!;
        var student25 = (await storage.Users.GetByLoginAsync("student25"))!;
        var student26 = (await storage.Users.GetByLoginAsync("student26"))!;
        var student30 = (await storage.Users.GetByLoginAsync("student30"))!;
        var student31 = (await storage.Users.GetByLoginAsync("student31"))!;
        var student32 = (await storage.Users.GetByLoginAsync("student32"))!;
        Assert.Equal(ik221!.Id, student01.GroupId);
        Assert.Equal(ik221.Id, student25.GroupId);
        Assert.Equal(ik222!.Id, student26.GroupId);
        Assert.Equal(ik222.Id, student30.GroupId);
        Assert.Null(student31.GroupId);
        Assert.Null(student32.GroupId);

        Assert.Equal("student01@example.com", student01.Email);
        Assert.Equal("Иванов Иван Иванович 01", student01.FullName);
        Assert.Equal("student32@example.com", (await storage.Users.GetByLoginAsync("student32"))!.Email);
        Assert.Equal("Иванов Иван Иванович 32", (await storage.Users.GetByLoginAsync("student32"))!.FullName);
    }

    [Fact]
    public async Task DemoSet_Labs_CompositionAndAttributes()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        await harness.Runner.RunAsync();

        // 20 работ семестра 1 + 3 работы семестра 2; за границами — нет.
        Assert.Equal(23, (await storage.Labs.GetAllAsync()).Count);
        Assert.NotNull(await await storage.Labs.TryGetByPairAsync(1, 1));
        Assert.NotNull(await await storage.Labs.TryGetByPairAsync(1, 20));
        Assert.Null(await await storage.Labs.TryGetByPairAsync(1, 21));
        Assert.NotNull(await await storage.Labs.TryGetByPairAsync(2, 3));
        Assert.Null(await await storage.Labs.TryGetByPairAsync(2, 4));

        // Содержание, защита у чётных, ссылка у кратных 5.
        var lab11 = (await storage.Labs.TryGetByPairAsync(1, 1))!;
        Assert.Equal("Содержание лабораторной работы №1", lab11.Content);
        Assert.False(lab11.DefenseRequired);
        Assert.Null(lab11.AssignmentUrl);

        var lab12 = (await storage.Labs.TryGetByPairAsync(1, 2))!;
        Assert.True(lab12.DefenseRequired);
        Assert.Null(lab12.AssignmentUrl);

        var lab15 = (await storage.Labs.TryGetByPairAsync(1, 5))!;
        Assert.False(lab15.DefenseRequired);
        Assert.Equal("https://git.example.com/assignments/1/5", lab15.AssignmentUrl);

        var lab110 = (await storage.Labs.TryGetByPairAsync(1, 10))!;
        Assert.True(lab110.DefenseRequired);
        Assert.Equal("https://git.example.com/assignments/1/10", lab110.AssignmentUrl);

        var lab21 = (await storage.Labs.TryGetByPairAsync(2, 1))!;
        Assert.Equal("Содержание лабораторной работы №1", lab21.Content);
        Assert.Null(lab21.AssignmentUrl);
    }

    [Fact]
    public async Task DemoSet_Submissions_ExactRulesWithTeacherStamp()
    {
        var storage = TestStorage.Create();
        var harness = CreateHarness(storage, environment: "Development", demoData: null);
        await harness.Runner.RunAsync();

        var teacher = (await storage.Users.GetByLoginAsync("teacher"))!;
        var student01 = (await storage.Users.GetByLoginAsync("student01"))!;
        var student02 = (await storage.Users.GetByLoginAsync("student02"))!;
        var expectedUpdatedAt = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

        var first = (await storage.Submissions.GetByStudentAndLabAsync(student01.Id, (await storage.Labs.TryGetByPairAsync(1, 1))!.Id))!;
        Assert.Equal(new DateOnly(2026, 9, 1), first.SubmitDate);
        Assert.Equal(new DateOnly(2026, 9, 11), first.DefenseDate);
        Assert.Equal(teacher.Id, first.UpdatedBy);
        Assert.Equal(expectedUpdatedAt, first.UpdatedAt);

        var second = (await storage.Submissions.GetByStudentAndLabAsync(student01.Id, (await storage.Labs.TryGetByPairAsync(1, 2))!.Id))!;
        Assert.Equal(new DateOnly(2026, 9, 2), second.SubmitDate);
        Assert.Equal(new DateOnly(2026, 9, 12), second.DefenseDate);
        Assert.Equal(teacher.Id, second.UpdatedBy);

        var third = (await storage.Submissions.GetByStudentAndLabAsync(student01.Id, (await storage.Labs.TryGetByPairAsync(1, 3))!.Id))!;
        Assert.Equal(new DateOnly(2026, 9, 3), third.SubmitDate);
        Assert.Null(third.DefenseDate);
        Assert.Equal(teacher.Id, third.UpdatedBy);

        var fourth = (await storage.Submissions.GetByStudentAndLabAsync(student02.Id, (await storage.Labs.TryGetByPairAsync(1, 1))!.Id))!;
        Assert.Equal(new DateOnly(2026, 9, 1), fourth.SubmitDate);
        Assert.Null(fourth.DefenseDate);
        Assert.Equal(teacher.Id, fourth.UpdatedBy);

        // Ровно 4 сид-сдачи.
        var allLabIds = (await storage.Labs.GetAllAsync()).Select(lab => lab.Id).ToList();
        Assert.Equal(4, (await storage.Submissions.ListByLabIdsAsync(allLabIds)).Count);

        // У других студентов сдач нет.
        Assert.Empty(await storage.Submissions.ListByStudentAsync((await storage.Users.GetByLoginAsync("student03"))!.Id));
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
        public Task RunAsync() => Runner.RunAsync();
    }

    private static long Delta(IKdfCounter counter, IReadOnlyDictionary<string, long> before, string caller) =>
        counter.Snapshot().GetValueOrDefault(caller) - before.GetValueOrDefault(caller);

    private static async Task AssertTeacherAsync(TestStorage storage, Pbkdf2PasswordHasher hasher)
    {
        var teacher = await storage.Users.GetByLoginAsync("teacher");
        Assert.NotNull(teacher);
        Assert.Equal(UserRoles.Teacher, teacher.Role);
        Assert.Null(teacher.GroupId);
        Assert.Equal("teacher@example.com", teacher.Email);
        Assert.True(hasher.Verify("teacher123!", teacher.PasswordHash, KdfCallers.Login));
    }

    private static async Task AssertDemoSetAsync(TestStorage storage)
    {
        Assert.Equal(32, (await storage.Users.ListStudentsAsync()).Count);
        Assert.Equal(3, (await storage.Groups.GetAllAsync()).Count);
        Assert.Equal(23, (await storage.Labs.GetAllAsync()).Count);
        var allLabIds = (await storage.Labs.GetAllAsync()).Select(lab => lab.Id).ToList();
        Assert.Equal(4, (await storage.Submissions.ListByLabIdsAsync(allLabIds)).Count);
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
