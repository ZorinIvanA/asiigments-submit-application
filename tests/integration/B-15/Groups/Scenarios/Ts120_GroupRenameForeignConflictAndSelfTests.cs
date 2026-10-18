using LabsApp.IntegrationTests.B15.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Groups.Scenarios;

/// <summary>
/// TS-120 «Группы: переименование, конфликт с чужим именем и с собственным»
/// (happy_path, FR-019, P1).
///
/// given: сессия teacher; существуют группы G1 ('А-1') и G2 ('Б-2') — создаются
///        POST /groups (демо-сид этих имён не содержит).
/// when:  PUT /groups/{G1} {name:'А-1-новое'}; PUT /groups/{G1} {name:'Б-2'};
///        PUT /groups/{G2} {name:'б-2'}.
/// then:  первый — 200 GroupDto с новым именем; второй — 409 'Группа с таким
///        названием уже существует' (имя занято ДРУГОЙ группой G2); третий —
///        200 (совпадение с самой собой без учёта регистра — не конфликт)
///        (FR-019: PUT, 409 «кроме самой группы»).
///
/// Шаги цепочки when исполняются в ОДНОМ тесте в порядке кейса: шаги мутируют
/// G1/G2 (переименования), поэтому разнесение по методам делало бы исход
/// зависимым от порядка исполнения тестов класса (общая IClassFixture-фикстура).
/// </summary>
public sealed class Ts120_GroupRenameForeignConflictAndSelfTests : IClassFixture<B15GroupsDemoDataFactory>
{
    private const string ConflictText = "Группа с таким названием уже существует";

    private readonly B15GroupsDemoDataFactory _factory;

    public Ts120_GroupRenameForeignConflictAndSelfTests(B15GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PutGroup_NewNameRenames_ForeignNameConflicts_OwnNameCaseInsensitiveOk()
    {
        // given: сессия teacher; группы G1 ('А-1') и G2 ('Б-2') существуют.
        using var client = B15GroupsSession.CreateTeacher(_factory);
        var g1Id = await CreateGroupAsync(client, "А-1");
        var g2Id = await CreateGroupAsync(client, "Б-2");

        // when: PUT /groups/{G1} {name:'А-1-новое'}.
        using var renamed = await B15GroupsApi.PutGroupAsync(client, g1Id, "А-1-новое");

        // then: 200 GroupDto с новым именем.
        using var renamedBody = await B15GroupsApi.ParseWithStatusAsync(
            renamed, HttpStatusCode.OK, $"PUT /groups/{g1Id} {{name:'А-1-новое'}} (teacher)");
        Assert.Equal(g1Id, B15GroupsApi.ReadString(renamedBody.RootElement, "id"));
        Assert.Equal("А-1-новое", B15GroupsApi.ReadString(renamedBody.RootElement, "name"));

        // when: PUT /groups/{G1} {name:'Б-2'} — имя занято ДРУГОЙ группой (G2).
        using var conflict = await B15GroupsApi.PutGroupAsync(client, g1Id, "Б-2");

        // then: 409 'Группа с таким названием уже существует'.
        using var conflictBody = await B15GroupsApi.ParseWithStatusAsync(
            conflict, HttpStatusCode.Conflict, $"PUT /groups/{g1Id} {{name:'Б-2'}} (teacher)");
        B15GroupsApi.MessageIs(conflictBody.RootElement, ConflictText);

        // when: PUT /groups/{G2} {name:'б-2'} — собственное имя в другом регистре.
        using var self = await B15GroupsApi.PutGroupAsync(client, g2Id, "б-2");

        // then: 200 — совпадение с самой собой (без учёта регистра) конфликтом
        // не считается (FR-019: 409 «кроме самой группы»).
        using var selfBody = await B15GroupsApi.ParseWithStatusAsync(
            self, HttpStatusCode.OK, $"PUT /groups/{g2Id} {{name:'б-2'}} (teacher)");
        Assert.Equal(g2Id, B15GroupsApi.ReadString(selfBody.RootElement, "id"));
        Assert.Equal("б-2", B15GroupsApi.ReadString(selfBody.RootElement, "name"));
    }

    /// <summary>Создаёт группу с данным именем и возвращает её id (шаг given).</summary>
    private static async Task<string> CreateGroupAsync(HttpClient client, string name)
    {
        using var created = await B15GroupsApi.PostGroupAsync(client, name);
        using var body = await B15GroupsApi.ParseWithStatusAsync(
            created, HttpStatusCode.Created, $"POST /groups {{name:'{name}'}} (given TS-120)");
        return B15GroupsApi.ReadString(body.RootElement, "id");
    }
}
