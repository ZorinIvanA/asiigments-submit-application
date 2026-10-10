using LabsApp.IntegrationTests.B15.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Groups.Scenarios;

/// <summary>
/// TS-121 «Группы: неизвестный id — 404 для PUT и DELETE» (negative, FR-019, P1).
///
/// given: сессия teacher; uuid, которого нет.
/// when:  PUT /groups/{uuid} {name:'X'}; DELETE /groups/{uuid}.
/// then:  оба — 404 'Группа не найдена'
///        (IF-010 NOT_FOUND_GROUP для PUT и DELETE /groups/{id}).
/// </summary>
public sealed class Ts121_GroupUnknownIdPutDeleteNotFoundTests : IClassFixture<B15GroupsDemoDataFactory>
{
    private const string NotFoundText = "Группа не найдена";

    private readonly B15GroupsDemoDataFactory _factory;

    public Ts121_GroupUnknownIdPutDeleteNotFoundTests(B15GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PutAndDeleteOfUnknownGroup_ReturnNotFound()
    {
        // given: сессия teacher; uuid, которого нет.
        using var client = B15GroupsSession.CreateTeacher(_factory);
        var unknownId = Guid.NewGuid().ToString();

        // when: PUT /groups/{uuid} {name:'X'}.
        using var put = await B15GroupsApi.PutGroupAsync(client, unknownId, "X");

        // then: 404 'Группа не найдена'.
        using var putBody = await B15GroupsApi.ParseWithStatusAsync(
            put, HttpStatusCode.NotFound, $"PUT /groups/{unknownId} {{name:'X'}} (teacher)");
        B15GroupsApi.MessageIs(putBody.RootElement, NotFoundText);

        // when: DELETE /groups/{uuid}.
        using var delete = await B15GroupsApi.DeleteGroupAsync(client, unknownId);

        // then: 404 'Группа не найдена'.
        using var deleteBody = await B15GroupsApi.ParseWithStatusAsync(
            delete, HttpStatusCode.NotFound, $"DELETE /groups/{unknownId} (teacher)");
        B15GroupsApi.MessageIs(deleteBody.RootElement, NotFoundText);
    }
}
