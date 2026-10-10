using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-150 (P0, happy_path; FR-021 AC «Ведомость первой страницы») «Ведомость:
/// первая страница — 5 студентов × 20 работ, total, page».
/// given: демо-сид: ИК-221 (25 студентов student01..student25), семестр 1 —
///        20 работ, 4 сид-сдачи у student01 (работы 1, 2, 3) и student02
///        (работа 1); сессия teacher.
/// when:  GET /api/v1/submissions?groupId=&lt;ИК-221&gt;&amp;semester=1&amp;page=1
/// then:  200: students.length=5 (первые по fullName↑, затем login↑, ru);
///        labs.length=20 (number↑, поля {id, number, defenseRequired});
///        submissions — только пары этих 5 студентов (сид-сдачи student01/02);
///        total=25; page=1.
/// </summary>
public sealed class Ts150_SubmissionsGridFirstPageSeedTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts150_SubmissionsGridFirstPageSeedTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FirstPage_ReturnsFiveSeedStudentsTwentyLabsSeedSubmissionsTotal25()
    {
        // given: ИК-221 (в сиде student01..student25); семестр 1 — 20 работ; teacher.
        var ik221 = B05SeedLookup.GroupByName(_factory, "ИК-221");
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var labsRepo = _factory.Services.GetRequiredService<ILabRepository>();
        Assert.Equal(25, users.ListStudents().Count(user => user.GroupId == ik221.Id));

        // Идентификаторы работ семестра 1 и ожидаемые сид-сдачи student01/02
        // (student01: работы 1, 2, 3; student02: работа 1).
        string LabId(int number) =>
            labsRepo.GetAll().Single(lab => lab.Semester == 1 && lab.Number == number).Id.ToString();
        var student01 = B05SeedLookup.StudentByLogin(_factory, "student01");
        var student02 = B05SeedLookup.StudentByLogin(_factory, "student02");
        var expectedSubmissions = new HashSet<(string StudentId, string LabId)>
        {
            (student01.Id.ToString(), LabId(1)),
            (student01.Id.ToString(), LabId(2)),
            (student01.Id.ToString(), LabId(3)),
            (student02.Id.ToString(), LabId(1)),
        };

        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: первая страница ведомости ИК-221 по семестру 1.
        using var response = await client.GetAsync(
            $"/api/v1/submissions?groupId={ik221.Id}&semester=1&page=1");

        // then: 200; total=25 (полное число студентов группы); page=1.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(25, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());

        // then: students — ровно 5 строк {id, fullName} в порядке fullName↑ (затем
        // login↑, ru-локаль): сидовые 'Иванов Иван Иванович 01'..'05' (student01..05).
        var students = root.GetProperty("students").EnumerateArray().ToList();
        Assert.Equal(5, students.Count);
        Assert.Equal(
            new[]
            {
                "Иванов Иван Иванович 01",
                "Иванов Иван Иванович 02",
                "Иванов Иван Иванович 03",
                "Иванов Иван Иванович 04",
                "Иванов Иван Иванович 05",
            },
            students.Select(item => item.GetProperty("fullName").GetString()).ToList());
        var expectedPageLogins = new[] { "student01", "student02", "student03", "student04", "student05" };
        Assert.Equal(
            expectedPageLogins,
            students
                .Select(item => users.GetById(Guid.Parse(item.GetProperty("id").GetString()!))?.Login)
                .ToList());
        foreach (var item in students)
        {
            BodyAssertions.HasExactlyProperties(item, "id", "fullName");
        }

        // then: labs — 20 строк {id, number, defenseRequired}, number↑ — работы семестра 1.
        var semesterLabIds = labsRepo.GetAll()
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
        // ровно 4 сид-сдачи student01/02.
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
}
