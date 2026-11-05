using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-114 (P0, happy_path; FR-021 AC «Ведомость первой страницы») «Ведомость:
/// первая страница — полная форма ответа (ru-порядок, дискриминирующие ФИО)».
/// given: ИК-221 (25 сид-студентов); два DI-сид-студента с ФИО, дающими РАЗНЫЙ
///        порядок при ru-культуре и ординальном сравнении — 'Авроров Иван'
///        (login avrorov) и 'Ёлкин Пётр' (login elkin), включены в ИК-221 через
///        PUT /students/{id}/group {groupId:ИК-221}; итого 27 студентов;
///        семестр 1 — 20 работ; 4 сид-сдачи у student01/02; у новых студентов
///        сдач нет; анонимный доступ (текущий пользователь — сид-преподаватель).
/// when:  GET /api/v1/submissions?groupId=&lt;ИК-221&gt;&amp;semester=1&amp;page=1
/// then:  200: students — 5 элементов {id, fullName} — в точности
///        ['Авроров Иван','Ёлкин Пётр','Иванов Иван Иванович 01',
///        'Иванов Иван Иванович 02','Иванов Иван Иванович 03'] (порядок fullName↑,
///        login↑, ru-локаль; иной порядок — провал); labs — 20 элементов
///        {id, number, defenseRequired} (number↑); submissions — только для пар
///        ЭТИХ 5 студентов × работы семестра (сид-сдачи student01/02; у
///        Авророва/Ёлкина пар нет); total=27; page=1.
/// </summary>
public sealed class Ts114_SubmissionsGridFirstPageRuOrderTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts114_SubmissionsGridFirstPageRuOrderTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FirstPage_ReturnsRuOrderedStudentsSemesterLabsAndSeedSubmissions()
    {
        // given: ИК-221; 20 работ семестра 1; анонимный доступ.
        var ik221 = await B05SeedLookup.GroupByNameAsync(_factory, "ИК-221");
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var labsRepo = _factory.Services.GetRequiredService<ILabRepository>();

        // given: два студента с дискриминирующими ФИО созданы DI-сидом
        // и включены в ИК-221 (PUT /students/{id}/group); итого в группе 27 студентов.
        var avrorov = await SeedStudentAsync("Авроров Иван", "avrorov", "avrorov@example.com");
        var elkin = await SeedStudentAsync("Ёлкин Пётр", "elkin", "elkin@example.com");
        using var client = HostClients.Create(_factory);
        foreach (var student in new[] { avrorov, elkin })
        {
            using var put = await client.PutAsJsonAsync(
                $"/api/v1/students/{student.Id}/group",
                new { groupId = ik221.Id.ToString() });
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        Assert.Equal(27, (await users.ListStudentsAsync()).Count(user => user.GroupId == ik221.Id));

        // Идентификаторы работ семестра 1 и ожидаемые сид-сдачи student01/02
        // (student01: работы 1, 2, 3; student02: работа 1).
        async Task<string> LabId(int number) =>
            (await labsRepo.GetAllAsync()).Single(lab => lab.Semester == 1 && lab.Number == number).Id.ToString();
        var student01 = await B05SeedLookup.StudentByLoginAsync(_factory, "student01");
        var student02 = await B05SeedLookup.StudentByLoginAsync(_factory, "student02");
        var expectedSubmissions = new HashSet<(string StudentId, string LabId)>();
expectedSubmissions.Add((student01.Id.ToString(), await LabId(1)));
expectedSubmissions.Add((student01.Id.ToString(), await LabId(2)));
expectedSubmissions.Add((student01.Id.ToString(), await LabId(3)));
expectedSubmissions.Add((student02.Id.ToString(), await LabId(1)));
        

        // when: первая страница ведомости ИК-221 по семестру 1.
        using var response = await client.GetAsync(
            $"/api/v1/submissions?groupId={ik221.Id}&semester=1&page=1");

        // then: 200; total=27 (полное число студентов группы); page=1.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(27, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());

        // then: students — ровно 5 строк {id, fullName} в ru-порядке fullName↑:
        // 'Авроров Иван' < 'Ёлкин Пётр' < 'Иванов …' по русской локали (ординальное
        // сравнение и инвариантная культура дали бы иной порядок — провал).
        var students = root.GetProperty("students").EnumerateArray().ToList();
        Assert.Equal(5, students.Count);
        Assert.Equal(
            new[]
            {
                "Авроров Иван",
                "Ёлкин Пётр",
                "Иванов Иван Иванович 01",
                "Иванов Иван Иванович 02",
                "Иванов Иван Иванович 03",
            },
            students.Select(item => item.GetProperty("fullName").GetString()).ToList());
        foreach (var item in students)
        {
            BodyAssertions.HasExactlyProperties(item, "id", "fullName");
        }

        // then: labs — 20 строк {id, number, defenseRequired}, number↑ — работы семестра 1.
        var semesterLabIds = (await labsRepo.GetAllAsync())
            .Where(lab => lab.Semester == 1)
            .Select(lab => lab.Id.ToString())
            .ToHashSet();
        Assert.Equal(20, semesterLabIds.Count);
        var labRows = root.GetProperty("labs").EnumerateArray().ToList();
        Assert.Equal(20, labRows.Count);
        Assert.Equal(
            Enumerable.Range(1, 20).ToList(),
            labRows.Select(item => item.GetProperty("number").GetInt32()).ToList());
        foreach (var item in labRows)
        {
            BodyAssertions.HasExactlyProperties(item, "id", "number", "defenseRequired");
        }

        Assert.True(
            semesterLabIds.SetEquals(labRows.Select(item => item.GetProperty("id").GetString()!).ToHashSet()),
            "Колонки ведомости — не работы семестра 1 (набор uuid работ расходится).");

        // then: submissions — только пары «эти 5 студентов × работы семестра»:
        // ровно 4 сид-сдачи student01/02; у Авророва/Ёлкина пар нет.
        var pageStudentIds = students
            .Select(item => item.GetProperty("id").GetString()!)
            .ToHashSet();
        var submissions = root.GetProperty("submissions").EnumerateArray().ToList();
        var actualSubmissions = new HashSet<(string StudentId, string LabId)>();
        foreach (var item in submissions)
        {
            var submissionStudentId = item.GetProperty("studentId").GetString();
            var submissionLabId = item.GetProperty("labId").GetString();
            Assert.True(
                submissionStudentId is not null && pageStudentIds.Contains(submissionStudentId),
                $"Ведомость содержит сдачу студента вне текущей страницы: {submissionStudentId}.");
            Assert.True(
                submissionLabId is not null && semesterLabIds.Contains(submissionLabId),
                $"Ведомость содержит сдачу работы вне семестра: {submissionLabId}.");
            actualSubmissions.Add((submissionStudentId!, submissionLabId!));
        }

        Assert.Equal(4, submissions.Count);
        Assert.True(
            expectedSubmissions.SetEquals(actualSubmissions),
            $"Ожидались ровно сид-сдачи student01/02, фактически: [{string.Join("; ", actualSubmissions)}].");
    }

    /// <summary>
    /// DI-сид студента (role=student, без группы): пароль по passwordHash не
    /// проверяется никогда (аутентификации в приложении нет), хэш — плейсхолдер сида.
    /// </summary>
    private async Task<User> SeedStudentAsync(string fullName, string login, string email)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = "!",
            FullName = fullName,
            Role = UserRoles.Student,
            GroupId = null,
            CreatedAt = DateTime.UtcNow,
        };
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        await users.AddAsync(user);
        return user;
    }
}
