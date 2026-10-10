using LabsApp.IntegrationTests.B15.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Groups.Scenarios;

/// <summary>
/// TS-124 «Группы: неизвестная группа состава — 404» (negative, FR-019, P1).
///
/// given: группы с указанным uuid не существует; сессия teacher.
/// when:  GET /groups/{несуществующий-uuid}/students.
/// then:  404 'Группа не найдена' (AC FR-019 «Неизвестная группа»,
///        IF-010 NOT_FOUND_GROUP).
/// </summary>
public sealed class Ts124_GroupRosterUnknownGroupTests : IClassFixture<B15GroupsDemoDataFactory>
{
    private const string NotFoundText = "Группа не найдена";

    private readonly B15GroupsDemoDataFactory _factory;

    public Ts124_GroupRosterUnknownGroupTests(B15GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRosterOfUnknownGroup_ReturnsNotFound()
    {
        // given: сессия teacher; группы с указанным uuid не существует.
        using var client = B15GroupsSession.CreateTeacher(_factory);
        var unknownId = Guid.NewGuid();

        // when: GET /groups/{несуществующий-uuid}/students.
        using var response = await B15GroupsApi.GetGroupStudentsAsync(client, unknownId.ToString());

        // then: 404 'Группа не найдена'.
        using var body = await B15GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.NotFound, $"GET /groups/{unknownId}/students (teacher)");
        B15GroupsApi.MessageIs(body.RootElement, NotFoundText);
    }
}
