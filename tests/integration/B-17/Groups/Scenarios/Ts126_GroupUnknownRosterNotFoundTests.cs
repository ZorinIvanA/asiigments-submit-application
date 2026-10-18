using LabsApp.IntegrationTests.B17.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Groups.Scenarios;

/// <summary>
/// TS-126 «Группы: неизвестная группа — 404» (negative, FR-019, P0).
///
/// given: группы с указанным uuid не существует (пустое хранилище с
///        сид-преподавателем).
/// when:  GET /groups/&lt;произвольный uuid&gt;/students.
/// then:  404; message «Группа не найдена» (NOT_FOUND_GROUP, FR-019 AC
///        «Неизвестная группа»).
/// </summary>
public sealed class Ts126_GroupUnknownRosterNotFoundTests : IClassFixture<B17GroupsWebAppFactory>
{
    private readonly B17GroupsWebAppFactory _factory;

    public Ts126_GroupUnknownRosterNotFoundTests(B17GroupsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRosterOfUnknownGroup_Returns404()
    {
        // given: сессия teacher; произвольный uuid несуществующей группы.
        using var client = B17GroupsSession.CreateTeacher(_factory);
        var unknownId = Guid.NewGuid();

        // when: GET /groups/<uuid>/students.
        using var response = await B17GroupsApi.GetGroupStudentsAsync(client, unknownId.ToString());

        // then: 404 «Группа не найдена».
        using var body = await B17GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.NotFound, $"GET /groups/{unknownId}/students (teacher)");
        B17GroupsApi.MessageIs(body.RootElement, "Группа не найдена");
    }
}
