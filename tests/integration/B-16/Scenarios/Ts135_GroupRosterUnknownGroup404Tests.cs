using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-135 «Группы: состав неизвестной группы — 404» (negative, FR-019, P0).
///
/// given: группы с указанным uuid не существует.
/// when:  GET /groups/&lt;случайный-uuid&gt;/students.
/// then:  404 'Группа не найдена' (FR-019 AC «Неизвестная группа»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts135_GroupRosterUnknownGroup404Tests : IClassFixture<B16GroupsDemoDataFactory>
{
    private const string NotFoundText = "Группа не найдена";

    private readonly B16GroupsDemoDataFactory _factory;

    public Ts135_GroupRosterUnknownGroup404Tests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRosterOfUnknownGroup_Returns404WithExactMessage()
    {
        // given: сессия teacher; случайный uuid, не принадлежащий никакой группе.
        using var client = B16GroupsSession.CreateTeacher(_factory);
        var unknownId = Guid.NewGuid();

        // when: GET /groups/<случайный-uuid>/students.
        using var response = await B16GroupsApi.GetGroupStudentsAsync(client, unknownId.ToString());

        // then: 404 'Группа не найдена'.
        using var body = await B16GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.NotFound, $"GET /groups/{unknownId}/students (teacher)");
        B16GroupsApi.MessageIs(body.RootElement, NotFoundText);
    }
}
