using LabsApp.IntegrationTests.B15.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Groups.Scenarios;

/// <summary>
/// TS-104 «groups: переименование — 200 / самоконфликт / 404 / 409»
/// (negative, FR-019, P1).
///
/// given: существует группа G1 с именем 'ИК-221' и группа 'ИК-222' (демо-сид);
///        сессия teacher. Примечание: кейс называет владельцем зоны
///        tests/integration/B-14/Groups (волна B-14); настоящий батч B-15
///        переиздаёт кейс в собственной зоне tests/integration/B-15/Groups —
///        поведенческая часть given/when/then исполняется дословно.
/// when:  PUT /groups/{G1.id} {name:'ик-221'} — текущее имя той же группы в
///        нижнем регистре; затем PUT /groups/{G1.id} {name:'Новое имя'};
///        PUT /groups/&lt;несуществующий-uuid&gt; {name:'X'};
///        PUT /groups/{G1.id} {name:'ик-222'}.
/// then:  PUT с собственным текущим именем (в любом регистре) — 200, НЕ 409
///        (собственная запись конфликтом не считается — паритет с FR-017/TS-097);
///        переименование в новое свободное имя — 200 GroupDto; несуществующий
///        id — 404 'Группа не найдена'; имя, занятое ДРУГОЙ группой ('ик-222') —
///        409 'Группа с таким названием уже существует'
///        (FR-019: 409 «кроме самой группы»).
///
/// Шаги цепочки when исполняются в ОДНОМ тесте в порядке кейса: шаги мутируют
/// G1 (переименование), поэтому разнесение по методам делало бы исход
/// зависимым от порядка исполнения тестов класса (общая IClassFixture-фикстура).
/// </summary>
public sealed class Ts104_GroupRenameSelfConflictNotFoundTests : IClassFixture<B15GroupsDemoDataFactory>
{
    private const string NotFoundText = "Группа не найдена";
    private const string ConflictText = "Группа с таким названием уже существует";

    private readonly B15GroupsDemoDataFactory _factory;

    public Ts104_GroupRenameSelfConflictNotFoundTests(B15GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PutGroup_OwnNameOk_NewNameRenames_UnknownNotFound_ForeignConflicts()
    {
        // given: сессия teacher; G1 = существующая группа с именем 'ИК-221'
        // (демо-сид); 'ИК-222' — другая существующая группа.
        using var client = B15GroupsSession.CreateTeacher(_factory);
        var g1Id = await B15GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: PUT /groups/{G1.id} {name:'ик-221'} — собственное текущее имя
        // той же группы в нижнем регистре.
        using var self = await B15GroupsApi.PutGroupAsync(client, g1Id, "ик-221");

        // then: 200, НЕ 409 — собственная запись конфликтом не считается
        // (паритет с FR-017/TS-097 «кроме самой группы»).
        using var selfBody = await B15GroupsApi.ParseWithStatusAsync(
            self, HttpStatusCode.OK, $"PUT /groups/{g1Id} {{name:'ик-221'}} — собственное имя (teacher)");
        Assert.Equal(g1Id, B15GroupsApi.ReadString(selfBody.RootElement, "id"));
        Assert.Equal("ик-221", B15GroupsApi.ReadString(selfBody.RootElement, "name"));

        // when: затем PUT /groups/{G1.id} {name:'Новое имя'} — переименование в
        // новое свободное имя.
        using var renamed = await B15GroupsApi.PutGroupAsync(client, g1Id, "Новое имя");

        // then: 200 GroupDto с новым именем.
        using var renamedBody = await B15GroupsApi.ParseWithStatusAsync(
            renamed, HttpStatusCode.OK, $"PUT /groups/{g1Id} {{name:'Новое имя'}} (teacher)");
        Assert.Equal(g1Id, B15GroupsApi.ReadString(renamedBody.RootElement, "id"));
        Assert.Equal("Новое имя", B15GroupsApi.ReadString(renamedBody.RootElement, "name"));

        // when: PUT /groups/<несуществующий-uuid> {name:'X'}.
        var unknownId = Guid.NewGuid();
        using var unknown = await B15GroupsApi.PutGroupAsync(client, unknownId.ToString(), "X");

        // then: 404 'Группа не найдена'.
        using var unknownBody = await B15GroupsApi.ParseWithStatusAsync(
            unknown, HttpStatusCode.NotFound, $"PUT /groups/{unknownId} {{name:'X'}} (teacher)");
        B15GroupsApi.MessageIs(unknownBody.RootElement, NotFoundText);

        // when: PUT /groups/{G1.id} {name:'ик-222'} — имя, занятое ДРУГОЙ группой.
        using var conflict = await B15GroupsApi.PutGroupAsync(client, g1Id, "ик-222");

        // then: 409 'Группа с таким названием уже существует'
        // (FR-019: 409 «кроме самой группы» — конфликтует только чужая запись).
        using var conflictBody = await B15GroupsApi.ParseWithStatusAsync(
            conflict, HttpStatusCode.Conflict, $"PUT /groups/{g1Id} {{name:'ик-222'}} (teacher)");
        B15GroupsApi.MessageIs(conflictBody.RootElement, ConflictText);
    }
}
