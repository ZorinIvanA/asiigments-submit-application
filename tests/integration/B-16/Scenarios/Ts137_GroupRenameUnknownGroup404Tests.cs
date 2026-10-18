using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-137 «Группы: переименование неизвестной группы — 404»
/// (negative, FR-019, P1).
///
/// given: случайный uuid (группы с таким id не существует); сессия teacher.
/// when:  PUT /groups/&lt;uuid&gt; {name:'X'}.
/// then:  404 'Группа не найдена' (FR-019).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts137_GroupRenameUnknownGroup404Tests : IClassFixture<B16GroupsDemoDataFactory>
{
    private const string NotFoundText = "Группа не найдена";

    private readonly B16GroupsDemoDataFactory _factory;

    public Ts137_GroupRenameUnknownGroup404Tests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PutGroupOfUnknownId_Returns404WithExactMessage()
    {
        // given: сессия teacher; случайный uuid.
        using var client = B16GroupsSession.CreateTeacher(_factory);
        var unknownId = Guid.NewGuid();

        // when: PUT /groups/<uuid> {name:'X'}.
        using var response = await B16GroupsApi.PutGroupAsync(client, unknownId.ToString(), "X");

        // then: 404 'Группа не найдена'.
        using var body = await B16GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.NotFound, $"PUT /groups/{unknownId} {{name:'X'}} (teacher)");
        B16GroupsApi.MessageIs(body.RootElement, NotFoundText);
    }
}
