using System.Text.Json;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-104 «groups: переименование — 200 / самоконфликт / 404 / 409 / 400
/// (невалидное имя)» (negative, FR-019, FR-023, P1; РЕВ-ISS-004).
///
/// given: существует группа G1 с именем «ИК-221» и группа «ИК-222» (демо-сид);
///        сессия teacher.
/// when:  PUT /groups/{G1.id} {name:'ик-221'} — текущее имя той же группы в
///        нижнем регистре; затем PUT /groups/{G1.id} {name:'Новое имя'};
///        PUT /groups/&lt;несуществующий-uuid&gt; {name:'X'};
///        PUT /groups/{G1.id} {name:'ик-222'}; затем PUT /groups/{G1.id}
///        {name:'   '} (только пробелы); отдельно PUT /groups/{G1.id}
///        {name:&lt;строка 101 символ&gt;}.
/// then:  PUT с собственным текущим именем (в любом регистре) — 200, НЕ 409
///        (собственная запись конфликтом не считается — паритет с FR-017/TS-097);
///        переименование в новое свободное имя — 200 GroupDto; несуществующий
///        id — 404 «Группа не найдена»; имя, занятое ДРУГОЙ группой («ик-222»),
///        — 409 «Группа с таким названием уже существует»; пробельное и
///        сверхдлинное имя — 400 «Данные заполнены неверно»,
///        errors.name=['Название группы — от 1 до 100 символов'] (единственный
///        текст ошибки имени — FR-019), G1 не переименована
///        (FR-019: 409 «кроме самой группы»; интерфейс PUT /api/v1/groups/{id}:
///        VALIDATION «правила имени»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts104_GroupRenameOkNotFoundConflictTests : IClassFixture<B16GroupsDemoDataFactory>
{
    private const string NotFoundText = "Группа не найдена";
    private const string ConflictText = "Группа с таким названием уже существует";
    private const string InvalidDataText = "Данные заполнены неверно";
    private const string NameValidationText = "Название группы — от 1 до 100 символов";

    private readonly B16GroupsDemoDataFactory _factory;

    public Ts104_GroupRenameOkNotFoundConflictTests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PutGroup_OwnNameOk_NewNameOk_Unknown404_Foreign409_InvalidName400()
    {
        // given: сессия teacher; G1 = «ИК-221» демо-сида; «ИК-222» существует.
        using var client = B16GroupsSession.CreateTeacher(_factory);
        var g1Id = await B16GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");
        var ik222Id = await B16GroupsApi.GetGroupIdByNameAsync(client, "ИК-222");

        // when: PUT /groups/{G1.id} {name:'ик-221'} — СОБСТВЕННОЕ текущее имя в
        // нижнем регистре.
        using var ownLower = await B16GroupsApi.PutGroupAsync(client, g1Id, "ик-221");

        // then: 200, НЕ 409 (собственная запись конфликтом не считается).
        using var ownLowerBody = await B16GroupsApi.ParseWithStatusAsync(
            ownLower, HttpStatusCode.OK, $"PUT /groups/{{name:'ик-221'}} собственной группы (teacher)");
        Assert.Equal(g1Id, B16GroupsApi.ReadString(ownLowerBody.RootElement, "id"));
        Assert.Equal("ик-221", B16GroupsApi.ReadString(ownLowerBody.RootElement, "name"));

        // when: PUT /groups/{G1.id} {name:'Новое имя'} — новое свободное имя.
        using var renamed = await B16GroupsApi.PutGroupAsync(client, g1Id, "Новое имя");

        // then: 200 GroupDto с новым именем.
        using var renamedBody = await B16GroupsApi.ParseWithStatusAsync(
            renamed, HttpStatusCode.OK, $"PUT /groups/{g1Id} {{name:'Новое имя'}} (teacher)");
        Assert.Equal(g1Id, B16GroupsApi.ReadString(renamedBody.RootElement, "id"));
        Assert.Equal("Новое имя", B16GroupsApi.ReadString(renamedBody.RootElement, "name"));

        // when: PUT /groups/<несуществующий-uuid> {name:'X'}.
        var unknownId = Guid.NewGuid();
        using var unknown = await B16GroupsApi.PutGroupAsync(client, unknownId.ToString(), "X");

        // then: 404 'Группа не найдена'.
        using var unknownBody = await B16GroupsApi.ParseWithStatusAsync(
            unknown, HttpStatusCode.NotFound, $"PUT /groups/{unknownId} {{name:'X'}} (teacher)");
        B16GroupsApi.MessageIs(unknownBody.RootElement, NotFoundText);

        // when: PUT /groups/{G1.id} {name:'ик-222'} — имя, занятое ДРУГОЙ группой
        // (без учёта регистра).
        using var conflict = await B16GroupsApi.PutGroupAsync(client, g1Id, "ик-222");

        // then: 409 'Группа с таким названием уже существует'.
        using var conflictBody = await B16GroupsApi.ParseWithStatusAsync(
            conflict, HttpStatusCode.Conflict, $"PUT /groups/{g1Id} {{name:'ик-222'}} (teacher)");
        B16GroupsApi.MessageIs(conflictBody.RootElement, ConflictText);

        // when: PUT /groups/{G1.id} {name:'   '} — только пробелы.
        using var blank = await B16GroupsApi.PutGroupAsync(client, g1Id, "   ");

        // then: 400 «Данные заполнены неверно», errors.name — единственный текст
        // имени (VALIDATION «правила имени», IF-010; РЕВ-ISS-004).
        using var blankBody = await B16GroupsApi.ParseWithStatusAsync(
            blank, HttpStatusCode.BadRequest, $"PUT /groups/{g1Id} {{name:'   '}} (teacher)");
        B16GroupsApi.MessageIs(blankBody.RootElement, InvalidDataText);
        B16GroupsApi.FieldErrorsExactly(blankBody.RootElement, "name", NameValidationText);

        // when: отдельно PUT /groups/{G1.id} {name:<строка 101 символ>}.
        var name101 = new string('Г', 101);
        Assert.Equal(101, name101.Length);
        using var overlong = await B16GroupsApi.PutGroupAsync(client, g1Id, name101);

        // then: 400, errors.name=['Название группы — от 1 до 100 символов'].
        using var overlongBody = await B16GroupsApi.ParseWithStatusAsync(
            overlong, HttpStatusCode.BadRequest, $"PUT /groups/{g1Id} {{name:101 символ}} (teacher)");
        B16GroupsApi.MessageIs(overlongBody.RootElement, InvalidDataText);
        B16GroupsApi.FieldErrorsExactly(overlongBody.RootElement, "name", NameValidationText);

        // then: G1 не переименована неудавшимися запросами — в списке групп G1
        // сохранил имя «Новое имя», «ИК-222» не изменилась.
        using var list = await B16GroupsApi.GetGroupsAsync(client);
        using var listBody = await B16GroupsApi.ParseWithStatusAsync(
            list, HttpStatusCode.OK, "GET /api/v1/groups (проверка неизменности после отказов)");
        var g1After = listBody.RootElement.EnumerateArray()
            .FirstOrDefault(group => B16GroupsApi.ReadString(group, "id") == g1Id);
        Assert.True(
            g1After.ValueKind == JsonValueKind.Object,
            $"Группа G1 ({g1Id}) исчезла из списка после отказов: {listBody.RootElement.GetRawText()}");
        Assert.Equal(
            "Новое имя",
            B16GroupsApi.ReadString(g1After, "name"));
        Assert.Equal(
            ik222Id,
            listBody.RootElement.EnumerateArray()
                .FirstOrDefault(group => B16GroupsApi.ReadString(group, "name") == "ИК-222")
                .GetProperty("id").GetString());
    }
}
