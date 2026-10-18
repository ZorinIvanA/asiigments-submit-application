using LabsApp.IntegrationTests.B02.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B02.Scenarios;

/// <summary>
/// TS-181 «Development с демо-данными: полный повтор сида мок-слоя» (FR-025 AC
/// «Старт с демо-данными (Development)», P1; тип happy_path).
///
/// given: чистое хранилище (отдельный хост); Development (дефолтный пароль
///        допустим — Seed__TeacherPassword не задан); Seed__DemoData=true (явно).
/// when:  старт приложения; вход teacher/teacher123!; инспекция хранилища
///        (репозитории из DI тестового хоста — те же singleton'ы, что читают
///        контроллеры).
/// then:  вход — 200; существуют 3 группы (ИК-221/222/223), 32 студента
///        (student01..student32; 01–25→ИК-221, 26–30→ИК-222, 31–32 без группы),
///        23 работы (семестр 1 №1–20, семестр 2 №1–3), 4 сдачи: student01 1.1
///        (2026-09-01/2026-09-11), student01 1.2 (2026-09-02/2026-09-12),
///        student01 1.3 (2026-09-03/null), student02 1.1 (2026-09-01/null).
/// </summary>
public sealed class Ts181_DevelopmentDemoDataSeedTests
{
    [Fact]
    public async Task DevelopmentWithDemoData_SeedsMockParitySet_LoginWorks()
    {
        // given: чистый Development-хост; Seed__DemoData=true задан явно
        // (useHarnessDefaults:false — харнес-умолчание «DemoData=false» не применяется);
        // Seed__TeacherPassword не задан — в Development действует дефолт 'teacher123!'.
        using var factory = new B02WebAppFactory(
            Environments.Development,
            useHarnessDefaults: false,
            settings: new Dictionary<string, string?>
            {
                ["Seed__DemoData"] = "true",
            });
        using var client = factory.CreateWarmClient();

        // when: вход teacher/teacher123!.
        using var login = await HostClients.LoginAsDefaultTeacherAsync(client);

        // then: вход — 200.
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // when: инспекция хранилища через DI тестового хоста.
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var groups = factory.Services.GetRequiredService<IGroupRepository>();
        var labs = factory.Services.GetRequiredService<ILabRepository>();
        var submissions = factory.Services.GetRequiredService<ISubmissionRepository>();

        // then: 3 группы — ИК-221/222/223.
        var allGroups = groups.GetAll();
        Assert.Equal(3, allGroups.Count);
        Assert.Equal(
            new[] { "ИК-221", "ИК-222", "ИК-223" }.OrderBy(name => name, StringComparer.Ordinal),
            allGroups.Select(group => group.Name).OrderBy(name => name, StringComparer.Ordinal));
        var groupNameById = allGroups.ToDictionary(group => group.Id, group => group.Name);

        // then: 32 студента student01..student32; 01–25→ИК-221, 26–30→ИК-222, 31–32 без группы.
        var students = users.ListStudents();
        Assert.Equal(32, students.Count);
        for (var number = 1; number <= 32; number++)
        {
            var studentLogin = $"student{number:00}";
            var student = students.SingleOrDefault(user => user.Login == studentLogin);
            Assert.NotNull(student);
            var expectedGroup = number <= 25 ? "ИК-221" : number <= 30 ? "ИК-222" : null;
            var actualGroup = student.GroupId is { } groupId ? groupNameById[groupId] : null;
            Assert.Equal(expectedGroup, actualGroup);
        }

        // then: 23 работы — семестр 1 №1–20, семестр 2 №1–3.
        var allLabs = labs.GetAll();
        Assert.Equal(23, allLabs.Count);
        var labPairs = allLabs.Select(lab => (lab.Semester, lab.Number)).ToHashSet();
        Assert.Equal(23, labPairs.Count);
        for (var number = 1; number <= 20; number++)
        {
            Assert.True(labPairs.Contains((1, number)), $"Работа 1:{number} отсутствует.");
        }

        for (var number = 1; number <= 3; number++)
        {
            Assert.True(labPairs.Contains((2, number)), $"Работа 2:{number} отсутствует.");
        }

        // then: 4 сдачи — student01 1.1/1.2/1.3 и student02 1.1 с точными датами.
        var stored = submissions.ListByLabIds(allLabs.Select(lab => lab.Id).ToList());
        var studentIdByLogin = students.ToDictionary(user => user.Login, user => user.Id);
        var labIdByPair = allLabs.ToDictionary(lab => (lab.Semester, lab.Number), lab => lab.Id);
        var actual = stored
            .Select(submission => string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{studentIdByLogin.Single(pair => pair.Value == submission.StudentId).Key}"
                + $":{labIdByPair.Single(pair => pair.Value == submission.LabId).Key.Semester}"
                + $".{labIdByPair.Single(pair => pair.Value == submission.LabId).Key.Number}"
                + $":{submission.SubmitDate:yyyy-MM-dd}"
                + $":{submission.DefenseDate:yyyy-MM-dd}"))
            .ToList();
        var expected = new[]
        {
            "student01:1.1:2026-09-01:2026-09-11",
            "student01:1.2:2026-09-02:2026-09-12",
            "student01:1.3:2026-09-03:",
            "student02:1.1:2026-09-01:",
        };
        Assert.True(
            expected.ToHashSet().SetEquals(actual),
            $"Сдачи не совпадают с сидом мок-слоя. Ожидались: {string.Join("; ", expected)}; "
            + "фактически: " + string.Join("; ", actual.OrderBy(item => item, StringComparer.Ordinal)) + ".");
    }
}
