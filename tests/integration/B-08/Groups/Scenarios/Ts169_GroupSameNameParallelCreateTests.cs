using LabsApp.IntegrationTests.B08.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Groups.Scenarios;

/// <summary>
/// TS-169 «Атомарность уникальности: параллельное создание групп с одним
/// именем» (concurrency, FR-024, FR-019, P1).
///
/// given: имя 'RACE-GRP' свободно; сессия teacher.
/// when:  два параллельных POST /groups {name:'RACE-GRP'}.
/// then:  ровно один 201, другой — 409 «Группа с таким названием уже
///        существует»; одна группа в списке (GET /groups).
/// </summary>
public sealed class Ts169_GroupSameNameParallelCreateTests : IClassFixture<B08GroupsWebAppFactory>
{
    private const string GroupName = "RACE-GRP";

    private readonly B08GroupsWebAppFactory _factory;

    public Ts169_GroupSameNameParallelCreateTests(B08GroupsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ParallelCreateSameGroupName_ExactlyOneCreatedOneConflict()
    {
        // given: teacher; имя 'RACE-GRP' свободно (хранилище без демо-набора).
        using var teacher = await B08GroupsClient.LoginAsTeacherAsync(_factory);
        var body = $"{{\"name\":{B08GroupsClient.JsonString(GroupName)}}}";

        // when: два параллельных POST /groups с одним именем.
        var firstTask = teacher.PostJsonAsync(B08GroupsClient.GroupsEndpoint, body);
        var secondTask = teacher.PostJsonAsync(B08GroupsClient.GroupsEndpoint, body);
        using var first = await firstTask;
        using var second = await secondTask;

        // then: ровно один 201, другой — 409.
        var statuses = new[] { first.StatusCode, second.StatusCode };
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Conflict));

        using var conflictBody = await B08GroupsClient.ReadJsonObjectAsync(
            first.StatusCode == HttpStatusCode.Conflict ? first : second,
            HttpStatusCode.Conflict,
            "проигравший параллельный POST /groups (TS-169)");
        Assert.Equal(
            B08GroupsClient.GroupNameDuplicateMessage,
            B08GroupsClient.StringProperty(conflictBody.RootElement, "message"));

        // then: в списке ровно одна группа с именем RACE-GRP.
        using var groups = await teacher.GetAsync(B08GroupsClient.GroupsEndpoint);
        using var groupsBody = await B08GroupsClient.ReadJsonArrayAsync(
            groups, HttpStatusCode.OK, "GET /groups после гонки (TS-169)");
        var raceGroups = groupsBody.RootElement.EnumerateArray()
            .Where(group => B08GroupsClient.StringProperty(group, "name") == GroupName)
            .ToList();
        Assert.Single(raceGroups);
    }
}
