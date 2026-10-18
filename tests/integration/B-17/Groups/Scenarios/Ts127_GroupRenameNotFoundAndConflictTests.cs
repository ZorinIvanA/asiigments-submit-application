using LabsApp.IntegrationTests.B17.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Groups.Scenarios;

/// <summary>
/// TS-127 «Группы: rename — 404 для неизвестной, 409 для занятого имени»
/// (negative, FR-019, P1).
///
/// given: существуют группы ИК-221 и ИК-222 (демо-сид); сессия teacher.
/// when:  PUT /groups/&lt;произвольный uuid&gt; {name:'X-1'}; затем
///        PUT /groups/{id ИК-222} {name:'ик-221'}.
/// then:  первый — 404 «Группа не найдена»; второй — 409 «Группа с таким
///        названием уже существует» (409 занятого ci-имени ДРУГОЙ группы —
///        «кроме самой группы», FR-019).
/// </summary>
public sealed class Ts127_GroupRenameNotFoundAndConflictTests : IClassFixture<B17GroupsDemoDataFactory>
{
    private readonly B17GroupsDemoDataFactory _factory;

    public Ts127_GroupRenameNotFoundAndConflictTests(B17GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PutUnknownGroup_Returns404_ThenPutForeignCiName_Returns409()
    {
        // given: сессия teacher; id группы ИК-222.
        using var client = B17GroupsSession.CreateTeacher(_factory);
        var ik222Id = await B17GroupsApi.GetGroupIdByNameAsync(client, "ИК-222");

        // when: PUT /groups/<произвольный uuid> {name:'X-1'}.
        var unknownId = Guid.NewGuid();
        using var notFound = await B17GroupsApi.PutGroupAsync(client, unknownId.ToString(), "X-1");

        // then: 404 «Группа не найдена».
        using var notFoundBody = await B17GroupsApi.ParseWithStatusAsync(
            notFound, HttpStatusCode.NotFound, $"PUT /groups/{unknownId} {{name:'X-1'}} (teacher)");
        B17GroupsApi.MessageIs(notFoundBody.RootElement, "Группа не найдена");

        // when: PUT /groups/{id ИК-222} {name:'ик-221'} — ci-имя занято ИК-221.
        using var conflict = await B17GroupsApi.PutGroupAsync(client, ik222Id, "ик-221");

        // then: 409 «Группа с таким названием уже существует».
        using var conflictBody = await B17GroupsApi.ParseWithStatusAsync(
            conflict, HttpStatusCode.Conflict, $"PUT /groups/{ik222Id} {{name:'ик-221'}} (teacher)");
        B17GroupsApi.MessageIs(conflictBody.RootElement, "Группа с таким названием уже существует");
    }
}
