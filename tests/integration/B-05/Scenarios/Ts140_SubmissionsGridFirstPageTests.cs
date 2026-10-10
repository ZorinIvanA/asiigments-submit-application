using System.Globalization;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-140 «Ведомость: первая страница» (happy_path, FR-021).
///
/// given: сид — ИК-221 (25 студентов), семестр 1 (20 работ), 4 сид-сдачи у
///        student01/02; сессия teacher (минт, ADR-015).
/// when:  GET /api/v1/submissions?groupId=&lt;ИК-221&gt;&amp;semester=1&amp;page=1
/// then:  200; students.length=5 (первые по fullName↑,login↑ ru-локаль —
///        ожидаемые порядок/идентификаторы вычисляются из сида той же
///        сортировкой); labs.length=20 (number↑); submissions — только для пар
///        «страница × работы семестра» (4 сид-сдачи student01/02 попали на
///        страницу); total=25; page=1 (FR-021 AC «Ведомость первой страницы»).
/// </summary>
public sealed class Ts140_SubmissionsGridFirstPageTests : IClassFixture<B05WebAppFactory>
{
    /// <summary>Сортировка списков ru-RU IgnoreCase (AR-005, Collation/SortComparer).</summary>
    private static readonly Comparer<string> RuNameComparer = Comparer<string>.Create((left, right) =>
        CultureInfo.GetCultureInfo("ru-RU").CompareInfo.Compare(left, right, CompareOptions.IgnoreCase));

    private readonly B05WebAppFactory _factory;

    public Ts140_SubmissionsGridFirstPageTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FirstPage_ReturnsFirstFiveStudentsSemesterLabsAndSeedSubmissions()
    {
        // given: ИК-221 с 25 студентами; 20 работ семестра 1; 4 сид-сдачи; teacher.
        var ik221 = B05SeedLookup.GroupByName(_factory, "ИК-221");
        var groupStudents = _factory.Services.GetRequiredService<IUserRepository>()
            .ListStudents()
            .Where(user => user.GroupId == ik221.Id)
            .ToList();
        Assert.Equal(25, groupStudents.Count);

        var expectedStudentIds = groupStudents
            .OrderBy(user => user.FullName, RuNameComparer)
            .ThenBy(user => user.Login, RuNameComparer)
            .Take(5)
            .Select(user => user.Id.ToString())
            .ToList();

        var labs = _factory.Services.GetRequiredService<ILabRepository>().GetAll();
        var semesterLabIds = labs
            .Where(lab => lab.Semester == 1)
            .Select(lab => lab.Id.ToString())
            .ToHashSet();
        Assert.Equal(20, semesterLabIds.Count);

        string LabId(int number) =>
            labs.Single(lab => lab.Semester == 1 && lab.Number == number).Id.ToString();
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

        // then: 200; страница 5 студентов, 20 работ, только парные сдачи, total=25, page=1.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(25, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());

        var students = root.GetProperty("students").EnumerateArray().ToList();
        Assert.Equal(5, students.Count);
        Assert.Equal(
            expectedStudentIds,
            students.Select(item => item.GetProperty("id").GetString()!).ToList());
        foreach (var item in students)
        {
            BodyAssertions.HasExactlyProperties(item, "id", "fullName");
        }

        var labRows = root.GetProperty("labs").EnumerateArray().ToList();
        Assert.Equal(20, labRows.Count);
        Assert.Equal(
            Enumerable.Range(1, 20).ToList(),
            labRows.Select(item => item.GetProperty("number").GetInt32()).ToList());
        var actualLabIds = labRows
            .Select(item => item.GetProperty("id").GetString()!)
            .ToHashSet();
        Assert.True(
            semesterLabIds.SetEquals(actualLabIds),
            "Колонки ведомости — не работы семестра 1 (набор uuid работ расходится).");

        var pageStudentIds = expectedStudentIds.ToHashSet();
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
