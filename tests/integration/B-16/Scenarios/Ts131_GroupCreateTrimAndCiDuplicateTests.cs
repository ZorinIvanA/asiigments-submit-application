using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-131 «Группы: создание с триммом и дубликат без учёта регистра»
/// (happy_path, FR-019, P0).
///
/// given: сессия teacher; группы ИК-224 не существует (демо-сид содержит только
///        ИК-221/222/223).
/// when:  POST /groups {name:'  ИК-224  '}; затем POST /groups {name:'ик-224'}.
/// then:  201 {name:'ИК-224', studentCount:0} — имя триммлено, счётчик пустой
///        группы; затем 409 «Группа с таким названием уже существует» — дубликат
///        lower(name) без учёта регистра (FR-019 AC «Создание и дубликат»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts131_GroupCreateTrimAndCiDuplicateTests : IClassFixture<B16GroupsDemoDataFactory>
{
    private const string ConflictText = "Группа с таким названием уже существует";

    private readonly B16GroupsDemoDataFactory _factory;

    public Ts131_GroupCreateTrimAndCiDuplicateTests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PostGroup_TrimsName_ThenLowercaseDuplicateConflicts()
    {
        // given: сессия teacher; группы ИК-224 не существует.
        using var client = B16GroupsSession.CreateTeacher(_factory);

        // when: POST /groups {name:'  ИК-224  '}.
        using var created = await B16GroupsApi.PostGroupAsync(client, "  ИК-224  ");

        // then: 201 {name:'ИК-224', studentCount:0}.
        using var createdBody = await B16GroupsApi.ParseWithStatusAsync(
            created, HttpStatusCode.Created, "POST /groups {name:'  ИК-224  '} (teacher)");
        Assert.Equal("ИК-224", B16GroupsApi.ReadString(createdBody.RootElement, "name"));
        Assert.Equal(0, B16GroupsApi.ReadInt(createdBody.RootElement, "studentCount"));
        Assert.True(
            Guid.TryParse(B16GroupsApi.ReadString(createdBody.RootElement, "id"), out _),
            "Ожидался uuid в поле id созданной GroupDto.");

        // when: POST /groups {name:'ик-224'} — дубликат без учёта регистра.
        using var duplicate = await B16GroupsApi.PostGroupAsync(client, "ик-224");

        // then: 409 'Группа с таким названием уже существует'.
        using var duplicateBody = await B16GroupsApi.ParseWithStatusAsync(
            duplicate, HttpStatusCode.Conflict, "POST /groups {name:'ик-224'} (teacher)");
        B16GroupsApi.MessageIs(duplicateBody.RootElement, ConflictText);
    }
}
