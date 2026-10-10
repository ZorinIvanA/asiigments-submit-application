using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-111 (P0, happy_path; FR-020 AC «Перевод в группу») «Студенты: включение/
/// перевод в группу».
/// given: student31 без группы; группа ИК-223 существует (демо-сид FR-004); сессия teacher.
/// when:  PUT /api/v1/students/{id31}/group {groupId:&lt;ИК-223.id&gt;}
/// then:  204; студент появляется в составе ИК-223 (GET /groups/{ИК-223.id}/students
///        содержит его).
/// </summary>
public sealed class Ts111_StudentAssignGroupTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts111_StudentAssignGroupTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutGroupUuid_TransfersStudentIntoIk223()
    {
        // given: student31 без группы; ИК-223 существует; teacher авторизован.
        var student31 = B05SeedLookup.StudentByLogin(_factory, "student31");
        Assert.Null(student31.GroupId);
        var ik223 = B05SeedLookup.GroupByName(_factory, "ИК-223");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: включение student31 в ИК-223.
        using var put = await client.PutAsJsonAsync(
            $"/api/v1/students/{student31.Id}/group",
            new { groupId = ik223.Id.ToString() });

        // then: 204; студент в составе ИК-223 (GET /groups/{id}/students).
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        using var roster = await client.GetAsync($"/api/v1/groups/{ik223.Id}/students");
        Assert.Equal(HttpStatusCode.OK, roster.StatusCode);
        var rosterRoot = await BodyAssertions.ReadRootObjectAsync(roster);
        var appears = rosterRoot.GetProperty("items").EnumerateArray().Any(item =>
            string.Equals(item.GetProperty("id").GetString(), student31.Id.ToString(), StringComparison.Ordinal)
            && string.Equals(item.GetProperty("login").GetString(), "student31", StringComparison.Ordinal));
        Assert.True(appears, "Студент student31 не появился в составе ИК-223.");
    }
}
