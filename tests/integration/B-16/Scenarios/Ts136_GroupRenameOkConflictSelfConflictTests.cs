using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-136 «Группы: переименование — успех, конфликт с чужим именем,
/// самоконфликт» (boundary, FR-019, P1).
///
/// given: существуют группы A и B (демо-сид: A=ИК-221, B=ИК-222); сессия teacher.
/// when:  PUT /groups/{A.id} {name:'Новое'}; PUT /groups/{A.id}
///        {name:'ИК-222'} (имя B); PUT /groups/{A.id} {name:'Новое'}
///        (текущее имя A).
/// then:  первый и третий — 200; второй — 409 «Группа с таким названием уже
///        существует» (409 кроме самой группы; FR-019).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts136_GroupRenameOkConflictSelfConflictTests : IClassFixture<B16GroupsDemoDataFactory>
{
    private const string ConflictText = "Группа с таким названием уже существует";

    private readonly B16GroupsDemoDataFactory _factory;

    public Ts136_GroupRenameOkConflictSelfConflictTests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PutGroup_RenameOk_ForeignName409_OwnCurrentNameOk()
    {
        // given: сессия teacher; существуют группы A (ИК-221) и B (ИК-222).
        using var client = B16GroupsSession.CreateTeacher(_factory);
        var aId = await B16GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");
        _ = await B16GroupsApi.GetGroupIdByNameAsync(client, "ИК-222");

        // when: PUT /groups/{A.id} {name:'Новое'} — переименование в свободное имя.
        using var renamed = await B16GroupsApi.PutGroupAsync(client, aId, "Новое");

        // then: 200 GroupDto с новым именем.
        using var renamedBody = await B16GroupsApi.ParseWithStatusAsync(
            renamed, HttpStatusCode.OK, $"PUT /groups/{aId} {{name:'Новое'}} (teacher)");
        Assert.Equal(aId, B16GroupsApi.ReadString(renamedBody.RootElement, "id"));
        Assert.Equal("Новое", B16GroupsApi.ReadString(renamedBody.RootElement, "name"));

        // when: PUT /groups/{A.id} {name:'ИК-222'} — имя, занятое группой B.
        using var conflict = await B16GroupsApi.PutGroupAsync(client, aId, "ИК-222");

        // then: 409 'Группа с таким названием уже существует'.
        using var conflictBody = await B16GroupsApi.ParseWithStatusAsync(
            conflict, HttpStatusCode.Conflict, $"PUT /groups/{aId} {{name:'ИК-222'}} (teacher)");
        B16GroupsApi.MessageIs(conflictBody.RootElement, ConflictText);

        // when: PUT /groups/{A.id} {name:'Новое'} — текущее имя самой группы A
        // (самоконфликт запрещён: 409 кроме самой группы).
        using var self = await B16GroupsApi.PutGroupAsync(client, aId, "Новое");

        // then: 200 (не 409).
        using var selfBody = await B16GroupsApi.ParseWithStatusAsync(
            self, HttpStatusCode.OK, $"PUT /groups/{aId} {{name:'Новое'}} собственной группы (teacher)");
        Assert.Equal(aId, B16GroupsApi.ReadString(selfBody.RootElement, "id"));
        Assert.Equal("Новое", B16GroupsApi.ReadString(selfBody.RootElement, "name"));
    }
}
