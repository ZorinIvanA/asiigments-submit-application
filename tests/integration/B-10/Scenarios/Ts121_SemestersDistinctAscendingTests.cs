using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-121 (P0, happy_path; FR-035) «GET /semesters: distinct по возрастанию, доступен student».
/// given: Лабы семестров 2,1,2 (DI-сид, ADR-010); сессия student.
/// when: GET /api/v1/semesters.
/// then: 200; тело [1,2] (массив целых, distinct, по возрастанию; роль student не мешает —
///       FR-035 AC «Distinct по возрастанию»).
/// </summary>
public sealed class Ts121_SemestersDistinctAscendingTests(B10NoDemoWebAppFactory factory) : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS121_GetSemesters_AsStudent_ReturnsDistinctAscending()
    {
        // given: DI-сид лаб семестров 2,1,2 и студента; сессия student (минт, ADR-022).
        B10Seed.AddLab(_factory, semester: 2, number: 1);
        B10Seed.AddLab(_factory, semester: 1, number: 1);
        B10Seed.AddLab(_factory, semester: 2, number: 2);
        B10Seed.AddStudent(_factory, B10Seed.StudentLogin);
        using var client = HostClients.CreateStudentClient(_factory, B10Seed.StudentLogin);

        // when: GET /api/v1/semesters.
        using var response = await client.GetAsync("/api/v1/semesters");

        // then: 200 [1,2] — массив целых, distinct, по возрастанию.
        var body = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(2, body.GetArrayLength());
        Assert.Equal(1, body[0].GetInt32());
        Assert.Equal(2, body[1].GetInt32());
    }
}
