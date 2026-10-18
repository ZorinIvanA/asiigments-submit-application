using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-148 (P0, happy_path; FR-060) «GET /submissions: ведомость группы со страницей
/// студентов pageSize=5».
/// given: Группа X с 7 студентами (известная сортировка fullName/login); у 4 есть
///        сдачи; в семестре 1 три работы №1–3; сессия teacher.
/// when: GET /api/v1/submissions?groupId=X&semester=1&page=2.
/// then: 200 SubmissionsGridDto: students — 2 записи (6-й и 7-й по сортировке),
///       total=7, page=2; labs — 3 по number↑; submissions — записи только для пар
///       (студенты текущей страницы × работы семестра). FR-060 AC «Ведомость».
/// </summary>
public sealed class Ts148_SubmissionsGridPage2Tests(B13WebAppFactory factory) : IClassFixture<B13WebAppFactory>
{
    private readonly B13WebAppFactory _factory = factory;

    [Fact]
    public async Task TS148_GridPage2_ReturnsSecondStudentPageWithFilteredSubmissions()
    {
        // given: группа X с 7 студентами (порядок: Алексеев…Жуков; страница 1 —
        // Алексеев/Борисов/Васильев/Григорьев/Дмитриев, страница 2 — Егоров/Жуков);
        // у 4 есть сдачи (двое на странице 1, двое на странице 2); лабы №1–3 семестра 1.
        var group = B13Seed.AddGroup(_factory, "Группа X TS-148");
        var students = B13Seed.AddSevenKnownOrderStudents(_factory, group.Id);
        var lab1 = B13Seed.AddLab(_factory, 1, 1);
        var lab2 = B13Seed.AddLab(_factory, 1, 2);
        var lab3 = B13Seed.AddLab(_factory, 1, 3);
        B13Seed.AddSubmission(_factory, students[1].Id, lab1.Id, "2025-02-10", null);        // Борисов — страница 1
        B13Seed.AddSubmission(_factory, students[2].Id, lab2.Id, "2025-02-11", null);        // Васильев — страница 1
        B13Seed.AddSubmission(_factory, students[5].Id, lab1.Id, "2025-02-12", "2025-02-20"); // Егоров — страница 2
        B13Seed.AddSubmission(_factory, students[6].Id, lab3.Id, "2025-02-13", null);        // Жуков — страница 2

        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /api/v1/submissions?groupId=X&semester=1&page=2.
        using var response = await client.GetAsync(
            $"/api/v1/submissions?groupId={Uri.EscapeDataString(group.Id.ToString())}&semester=1&page=2");

        // then: 200 SubmissionsGridDto ровно {students, labs, submissions, total, page}.
        var body = await ApiAssert.ReadOkJsonAsync(response);
        ApiAssert.HasExactlyProperties(body, "students", "labs", "submissions", "total", "page");
        Assert.Equal(7, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("page").GetInt32());

        // then: students — 2 записи (6-й и 7-й по сортировке fullName ci (ru)): Егоров, Жуков.
        var pageStudents = body.GetProperty("students");
        Assert.Equal(JsonValueKind.Array, pageStudents.ValueKind);
        Assert.Equal(2, pageStudents.GetArrayLength());
        Assert.Equal(students[5].Id.ToString(), pageStudents[0].GetProperty("id").GetString());
        Assert.Equal("Егоров", pageStudents[0].GetProperty("fullName").GetString());
        Assert.Equal(students[6].Id.ToString(), pageStudents[1].GetProperty("id").GetString());
        Assert.Equal("Жуков", pageStudents[1].GetProperty("fullName").GetString());
        foreach (var student in pageStudents.EnumerateArray())
        {
            ApiAssert.HasExactlyProperties(student, "id", "fullName");
        }

        // then: labs — 3 работы семестра по number↑: №1, №2, №3.
        var labs = body.GetProperty("labs");
        Assert.Equal(JsonValueKind.Array, labs.ValueKind);
        Assert.Equal(3, labs.GetArrayLength());
        Assert.Equal(lab1.Id.ToString(), labs[0].GetProperty("id").GetString());
        Assert.Equal(1, labs[0].GetProperty("number").GetInt32());
        Assert.Equal(lab2.Id.ToString(), labs[1].GetProperty("id").GetString());
        Assert.Equal(2, labs[1].GetProperty("number").GetInt32());
        Assert.Equal(lab3.Id.ToString(), labs[2].GetProperty("id").GetString());
        Assert.Equal(3, labs[2].GetProperty("number").GetInt32());
        foreach (var lab in labs.EnumerateArray())
        {
            ApiAssert.HasExactlyProperties(lab, "id", "number", "defenseRequired");
        }

        // then: submissions — записи только для пар (студенты текущей страницы ×
        // работы семестра): из 4 сид-сдач попадают ровно 2 (Егоров→№1, Жуков→№3);
        // сдачи студентов страницы 1 (Борисов→№1, Васильев→№2) не отдаются.
        var submissions = body.GetProperty("submissions");
        Assert.Equal(JsonValueKind.Array, submissions.ValueKind);
        Assert.Equal(2, submissions.GetArrayLength());
        var actualPairs = submissions.EnumerateArray()
            .Select(submission => (
                StudentId: submission.GetProperty("studentId").GetString() ?? string.Empty,
                LabId: submission.GetProperty("labId").GetString() ?? string.Empty))
            .OrderBy(pair => pair.StudentId, StringComparer.Ordinal)
            .ThenBy(pair => pair.LabId, StringComparer.Ordinal)
            .ToList();
        var expectedPairs = new[]
        {
            (StudentId: students[5].Id.ToString(), LabId: lab1.Id.ToString()),
            (StudentId: students[6].Id.ToString(), LabId: lab3.Id.ToString()),
        }
            .OrderBy(pair => pair.StudentId, StringComparer.Ordinal)
            .ThenBy(pair => pair.LabId, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(expectedPairs, actualPairs);
        var pageStudentIds = new[] { students[5].Id.ToString(), students[6].Id.ToString() };
        var semesterLabIds = new[] { lab1.Id.ToString(), lab2.Id.ToString(), lab3.Id.ToString() };
        foreach (var submission in submissions.EnumerateArray())
        {
            ApiAssert.HasExactlyProperties(submission, "studentId", "labId", "submitDate", "defenseDate");
            Assert.Contains(submission.GetProperty("studentId").GetString(), pageStudentIds);
            Assert.Contains(submission.GetProperty("labId").GetString(), semesterLabIds);
        }

        // then: даты записи Егорова переданы как есть ('YYYY-MM-DD', defenseDate не null).
        var egorovRecord = submissions.EnumerateArray()
            .Single(submission => submission.GetProperty("studentId").GetString() == students[5].Id.ToString());
        Assert.Equal("2025-02-12", egorovRecord.GetProperty("submitDate").GetString());
        Assert.Equal("2025-02-20", egorovRecord.GetProperty("defenseDate").GetString());
    }
}
