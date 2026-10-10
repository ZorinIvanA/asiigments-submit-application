using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-135 «Студенты: перевод в группу» (happy_path, FR-020).
///
/// given: student31 без группы; ИК-223 существует (демо-сид FR-004); сессия
///        teacher (минт, ADR-015).
/// when:  PUT /api/v1/students/{id31}/group {groupId:&lt;uuid ИК-223&gt;}
/// then:  204; студент появляется в составе ИК-223 — проверка через
///        GET /api/v1/groups/{ИК-223}/students (FR-020 AC «Перевод в группу»).
/// </summary>
public sealed class Ts135_StudentsAssignGroupTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts135_StudentsAssignGroupTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutGroupUuid_TransfersStudentIntoIk223()
    {
        // given: student31 без группы; ИК-223 существует; teacher авторизован.
        var student31 = B05SeedLookup.StudentByLogin(_factory, "student31");
        Assert.Null(student31.GroupId);
        var ik223 = B05SeedLookup.GroupByName(_factory, "ИК-223");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: назначение student31 в ИК-223.
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
        Assert.True(appears, "Переведённый студент не появился в составе ИК-223.");
    }
}
