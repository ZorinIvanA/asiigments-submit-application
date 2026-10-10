using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-159 (P0, happy_path; FR-062) «GET /me/submissions: студент в группе видит
/// только свои сдачи».
/// given: Студент в группе; в семестре 1 две работы; сдача студента по одной;
///        чужая сдача по другой (другой студент той же группы); сессия студента.
/// when: GET /api/v1/me/submissions?semester=1.
/// then: 200; hasGroup=true; labs — 2 по number↑; submissions — ровно 1 запись
///       студента (данные чужих студентов не отдаются). FR-062 AC «Студент в группе».
/// </summary>
public sealed class Ts159_MeSubmissionsOwnOnlyTests(B13WebAppFactory factory) : IClassFixture<B13WebAppFactory>
{
    private readonly B13WebAppFactory _factory = factory;

    [Fact]
    public async Task TS159_MeSubmissions_ReturnsOnlyOwnSubmissionWithoutForeignData()
    {
        // given: студент в группе; две работы семестра 1; своя сдача по №1,
        // чужая сдача (другой студент той же группы) по №2; сессия студента.
        var group = B13Seed.AddGroup(_factory, "Группа TS-159");
        var student = B13Seed.AddStudent(_factory, "ts159-student", "Алексеев", group.Id);
        var foreign = B13Seed.AddStudent(_factory, "ts159-foreign", "Борисов", group.Id);
        var lab1 = B13Seed.AddLab(_factory, 1, 1);
        var lab2 = B13Seed.AddLab(_factory, 1, 2);
        B13Seed.AddSubmission(_factory, student.Id, lab1.Id, "2025-04-01", null);
        B13Seed.AddSubmission(_factory, foreign.Id, lab2.Id, "2025-04-02", "2025-04-09");
        using var client = HostClients.CreateStudentClient(_factory, "ts159-student");

        // when: GET /api/v1/me/submissions?semester=1.
        using var response = await client.GetAsync("/api/v1/me/submissions?semester=1");

        // then: 200; hasGroup=true; тело ровно {hasGroup, labs, submissions}.
        var body = await ApiAssert.ReadOkJsonAsync(response);
        ApiAssert.HasExactlyProperties(body, "hasGroup", "labs", "submissions");
        Assert.True(body.GetProperty("hasGroup").GetBoolean());

        // then: labs — 2 работы семестра по number↑: №1, №2.
        var labs = body.GetProperty("labs");
        Assert.Equal(2, labs.GetArrayLength());
        Assert.Equal(lab1.Id.ToString(), labs[0].GetProperty("id").GetString());
        Assert.Equal(1, labs[0].GetProperty("number").GetInt32());
        Assert.Equal(lab2.Id.ToString(), labs[1].GetProperty("id").GetString());
        Assert.Equal(2, labs[1].GetProperty("number").GetInt32());

        // then: submissions — ровно 1 запись студента (по работе №1); чужая сдача
        // (Борисов по №2) не отдаётся; чужих данных нет — запись без studentId,
        // ровно {labId, submitDate, defenseDate}.
        var submissions = body.GetProperty("submissions");
        Assert.Equal(1, submissions.GetArrayLength());
        var own = submissions[0];
        ApiAssert.HasExactlyProperties(own, "labId", "submitDate", "defenseDate");
        Assert.Equal(lab1.Id.ToString(), own.GetProperty("labId").GetString());
        Assert.Equal("2025-04-01", own.GetProperty("submitDate").GetString());
        Assert.Equal(JsonValueKind.Null, own.GetProperty("defenseDate").ValueKind);
    }
}
