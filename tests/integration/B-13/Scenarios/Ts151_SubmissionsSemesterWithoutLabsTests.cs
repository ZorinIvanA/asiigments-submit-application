using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-151 (P1, boundary; FR-060) «GET /submissions: семестр без работ — labs=[] и
/// submissions=[]».
/// given: Группа X со студентами; в семестре 5 работ нет (работа есть только в
///        семестре 1 — фильтр по семестру наблюдаем).
/// when: GET /api/v1/submissions?groupId=X&amp;semester=5.
/// then: 200: labs=[], submissions=[], students и total корректны по группе.
///       FR-060 AC «Семестр без работ».
/// </summary>
public sealed class Ts151_SubmissionsSemesterWithoutLabsTests(B13WebAppFactory factory) : IClassFixture<B13WebAppFactory>
{
    private readonly B13WebAppFactory _factory = factory;

    [Fact]
    public async Task TS151_SemesterWithoutLabs_ReturnsEmptyLabsAndSubmissions()
    {
        // given: группа с двумя студентами; работа есть только в семестре 1.
        var group = B13Seed.AddGroup(_factory, "Группа TS-151");
        var first = B13Seed.AddStudent(_factory, "ts151-s1", "Алексеев", group.Id);
        var second = B13Seed.AddStudent(_factory, "ts151-s2", "Борисов", group.Id);
        B13Seed.AddLab(_factory, 1, 1);
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /submissions?groupId=X&semester=5.
        using var response = await client.GetAsync(
            $"/api/v1/submissions?groupId={Uri.EscapeDataString(group.Id.ToString())}&semester=5");

        // then: 200: labs=[], submissions=[], students и total корректны по группе.
        var body = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("labs").ValueKind);
        Assert.Equal(0, body.GetProperty("labs").GetArrayLength());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("submissions").ValueKind);
        Assert.Equal(0, body.GetProperty("submissions").GetArrayLength());
        Assert.Equal(2, body.GetProperty("total").GetInt32());
        var students = body.GetProperty("students");
        Assert.Equal(2, students.GetArrayLength());
        Assert.Equal(first.Id.ToString(), students[0].GetProperty("id").GetString());
        Assert.Equal("Алексеев", students[0].GetProperty("fullName").GetString());
        Assert.Equal(second.Id.ToString(), students[1].GetProperty("id").GetString());
        Assert.Equal("Борисов", students[1].GetProperty("fullName").GetString());
    }
}
