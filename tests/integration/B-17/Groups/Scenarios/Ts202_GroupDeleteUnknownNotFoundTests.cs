using LabsApp.IntegrationTests.B17.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Groups.Scenarios;

/// <summary>
/// TS-202 «Группы: DELETE несуществующего id — 404» (negative, FR-019, P1).
///
/// given: группы с указанным uuid не существует; сессия teacher (пустое
///        хранилище с сид-преподавателем).
/// when:  DELETE /api/v1/groups/&lt;произвольный uuid&gt;.
/// then:  404; message «Группа не найдена» (NOT_FOUND_GROUP; FR-019:
///        «DELETE /groups/{id} → 204 …, 404 при неизвестном id»).
/// </summary>
public sealed class Ts202_GroupDeleteUnknownNotFoundTests : IClassFixture<B17GroupsWebAppFactory>
{
    private readonly B17GroupsWebAppFactory _factory;

    public Ts202_GroupDeleteUnknownNotFoundTests(B17GroupsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task DeleteUnknownGroup_Returns404()
    {
        // given: сессия teacher; произвольный uuid несуществующей группы.
        using var client = B17GroupsSession.CreateTeacher(_factory);
        var unknownId = Guid.NewGuid();

        // when: DELETE /groups/<uuid>.
        using var response = await B17GroupsApi.DeleteGroupAsync(client, unknownId.ToString());

        // then: 404 «Группа не найдена».
        using var body = await B17GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.NotFound, $"DELETE /groups/{unknownId} (teacher)");
        B17GroupsApi.MessageIs(body.RootElement, "Группа не найдена");
    }
}
