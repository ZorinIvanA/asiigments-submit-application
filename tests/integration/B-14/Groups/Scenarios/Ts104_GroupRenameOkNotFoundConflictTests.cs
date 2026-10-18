using LabsApp.IntegrationTests.B14.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Groups.Scenarios;

/// <summary>
/// TS-104 «groups: переименование — 200 / 404 / 409» (negative, FR-019, P1).
///
/// given: существует группа G1 и группа 'ИК-222' (демо-сид; G1 = ИК-221);
///        сессия teacher.
/// when:  PUT /groups/{G1.id} {name:'Новое имя'}; PUT /groups/<несуществующий-uuid>
///        {name:'X'}; PUT /groups/{G1.id} {name:'ик-222'}.
/// then:  200 GroupDto; 404 'Группа не найдена'; 409 'Группа с таким названием
///        уже существует' — конфликт чужого имени; собственное имя конфликтом
///        не считается (FR-019).
/// </summary>
public sealed class Ts104_GroupRenameOkNotFoundConflictTests : IClassFixture<B14GroupsDemoDataFactory>
{
    private const string NotFoundText = "Группа не найдена";
    private const string ConflictText = "Группа с таким названием уже существует";

    private readonly B14GroupsDemoDataFactory _factory;

    public Ts104_GroupRenameOkNotFoundConflictTests(B14GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PutGroup_Renames_UnknownIdNotFound_ForeignNameConflicts()
    {
        // given: сессия teacher; G1 = существующая группа (ИК-221 демо-сида).
        using var client = B14GroupsSession.CreateTeacher(_factory);
        var g1Id = await B14GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: PUT /groups/{G1.id} {name:'Новое имя'}.
        using var renamed = await B14GroupsApi.PutGroupAsync(client, g1Id, "Новое имя");

        // then: 200 GroupDto с новым именем.
        using var renamedBody = await B14GroupsApi.ParseWithStatusAsync(
            renamed, HttpStatusCode.OK, $"PUT /groups/{g1Id} {{name:'Новое имя'}} (teacher)");
        Assert.Equal(g1Id, B14GroupsApi.ReadString(renamedBody.RootElement, "id"));
        Assert.Equal("Новое имя", B14GroupsApi.ReadString(renamedBody.RootElement, "name"));

        // when: PUT /groups/<несуществующий-uuid> {name:'X'}.
        var unknownId = Guid.NewGuid();
        using var unknown = await B14GroupsApi.PutGroupAsync(client, unknownId.ToString(), "X");

        // then: 404 'Группа не найдена'.
        using var unknownBody = await B14GroupsApi.ParseWithStatusAsync(
            unknown, HttpStatusCode.NotFound, $"PUT /groups/{unknownId} {{name:'X'}} (teacher)");
        B14GroupsApi.MessageIs(unknownBody.RootElement, NotFoundText);

        // when: PUT /groups/{G1.id} {name:'ик-222'} — чужое имя без учёта регистра
        // (собственное имя G1 конфликтом не считается).
        using var conflict = await B14GroupsApi.PutGroupAsync(client, g1Id, "ик-222");

        // then: 409 'Группа с таким названием уже существует'.
        using var conflictBody = await B14GroupsApi.ParseWithStatusAsync(
            conflict, HttpStatusCode.Conflict, $"PUT /groups/{g1Id} {{name:'ик-222'}} (teacher)");
        B14GroupsApi.MessageIs(conflictBody.RootElement, ConflictText);
    }

    [Fact]
    public async Task PutGroup_OwnNameIsNotAConflict()
    {
        // given: сессия teacher; группа с известным именем (ИК-222 демо-сида).
        using var client = B14GroupsSession.CreateTeacher(_factory);
        var groupId = await B14GroupsApi.GetGroupIdByNameAsync(client, "ИК-222");

        // when: PUT /groups/{id} {name:'ИК-222'} — переименование в СОБСТВЕННОЕ имя.
        using var response = await B14GroupsApi.PutGroupAsync(client, groupId, "ИК-222");

        // then: 200 (собственное имя конфликтом не считается).
        using var body = await B14GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"PUT /groups/{groupId} {{name:'ИК-222'}} (teacher)");
        Assert.Equal("ИК-222", B14GroupsApi.ReadString(body.RootElement, "name"));
    }
}
