using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-160 (P0, boundary; FR-062) «GET /me/submissions: студент без группы».
/// given: Студент без группы; сессия студента.
/// when: GET /api/v1/me/submissions?semester=1.
/// then: 200 {hasGroup:false, labs:[], submissions:[]}. FR-062 AC «Без группы».
/// </summary>
public sealed class Ts160_MeSubmissionsNoGroupTests(B13WebAppFactory factory) : IClassFixture<B13WebAppFactory>
{
    private readonly B13WebAppFactory _factory = factory;

    [Fact]
    public async Task TS160_MeSubmissions_StudentWithoutGroup_ReturnsHasGroupFalseWithEmptyLists()
    {
        // given: студент без группы (GroupId = null); сессия студента.
        B13Seed.AddStudent(_factory, "ts160-loner", "Одиноков", null);
        using var client = HostClients.CreateStudentClient(_factory, "ts160-loner");

        // when: GET /me/submissions?semester=1.
        using var response = await client.GetAsync("/api/v1/me/submissions?semester=1");

        // then: 200 ровно {hasGroup:false, labs:[], submissions:[]}.
        var body = await ApiAssert.ReadOkJsonAsync(response);
        ApiAssert.HasExactlyProperties(body, "hasGroup", "labs", "submissions");
        Assert.False(body.GetProperty("hasGroup").GetBoolean());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("labs").ValueKind);
        Assert.Equal(0, body.GetProperty("labs").GetArrayLength());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("submissions").ValueKind);
        Assert.Equal(0, body.GetProperty("submissions").GetArrayLength());
    }
}
