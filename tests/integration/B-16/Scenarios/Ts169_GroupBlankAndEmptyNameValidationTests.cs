using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-169 «groups: пустое и пробельное имя — 400 с единственным текстом ошибки
/// имени (ревью R4a)» (boundary, FR-019, FR-023, P1).
///
/// given: сессия teacher; групп с пробельными именами не существует (фикстура
///        без демо-сида: чистое хранилище, только сид-преподаватель).
/// when:  POST /api/v1/groups {name:'   '} (только пробелы); отдельно POST
///        {name:''} (пустая строка).
/// then:  оба — 400 «Данные заполнены неверно», errors.name=['Название группы —
///        от 1 до 100 символов'] — единственный текст ошибки имени группы:
///        FR-019 «name 1–100 после трима, иначе 400 errors.name=['Название
///        группы — от 1 до 100 символов']» (текст «Заполните поле» для имени
///        группы словарём не предусмотрен); группа не создана (GET /groups не
///        содержит новых записей).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts169_GroupBlankAndEmptyNameValidationTests : IClassFixture<B16GroupsWebAppFactory>
{
    private const string InvalidDataText = "Данные заполнены неверно";
    private const string NameValidationText = "Название группы — от 1 до 100 символов";

    private readonly B16GroupsWebAppFactory _factory;

    public Ts169_GroupBlankAndEmptyNameValidationTests(B16GroupsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PostGroup_BlankAndEmptyName_SingleNameError_NoGroupCreated()
    {
        // given: сессия teacher; снимок перечня групп до запросов (чистое
        // хранилище фикстуры — групп с пробельными именами не существует).
        using var client = B16GroupsSession.CreateTeacher(_factory);
        using var before = await B16GroupsApi.GetGroupsAsync(client);
        using var beforeBody = await B16GroupsApi.ParseWithStatusAsync(
            before, HttpStatusCode.OK, "GET /api/v1/groups (снимок до POST)");
        var namesBefore = beforeBody.RootElement.EnumerateArray()
            .Select(group => B16GroupsApi.ReadString(group, "name"))
            .ToList();

        // when: POST /api/v1/groups {name:'   '} — только пробелы.
        using var blank = await B16GroupsApi.PostGroupAsync(client, "   ");

        // then: 400 «Данные заполнены неверно», errors.name — ровно один текст.
        using var blankBody = await B16GroupsApi.ParseWithStatusAsync(
            blank, HttpStatusCode.BadRequest, "POST /groups {name:'   '} (teacher)");
        B16GroupsApi.MessageIs(blankBody.RootElement, InvalidDataText);
        B16GroupsApi.FieldErrorsExactly(blankBody.RootElement, "name", NameValidationText);

        // when: отдельно POST {name:''} — пустая строка.
        using var empty = await B16GroupsApi.PostGroupAsync(client, string.Empty);

        // then: 400, errors.name=['Название группы — от 1 до 100 символов'].
        using var emptyBody = await B16GroupsApi.ParseWithStatusAsync(
            empty, HttpStatusCode.BadRequest, "POST /groups {name:''} (teacher)");
        B16GroupsApi.MessageIs(emptyBody.RootElement, InvalidDataText);
        B16GroupsApi.FieldErrorsExactly(emptyBody.RootElement, "name", NameValidationText);

        // then: группа не создана — GET /groups не содержит новых записей
        // (перечень совпадает со снимком до запросов).
        using var after = await B16GroupsApi.GetGroupsAsync(client);
        using var afterBody = await B16GroupsApi.ParseWithStatusAsync(
            after, HttpStatusCode.OK, "GET /api/v1/groups (проверка «не создана»)");
        var namesAfter = afterBody.RootElement.EnumerateArray()
            .Select(group => B16GroupsApi.ReadString(group, "name"))
            .ToList();
        Assert.True(
            namesBefore.SequenceEqual(namesAfter),
            $"Ожидался неизменный перечень групп после 400-ответов, было [{string.Join(", ", namesBefore)}], " +
            $"стало [{string.Join(", ", namesAfter)}].");
    }
}
