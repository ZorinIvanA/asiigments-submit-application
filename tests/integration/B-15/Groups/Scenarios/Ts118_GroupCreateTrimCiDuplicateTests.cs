using LabsApp.IntegrationTests.B15.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Groups.Scenarios;

/// <summary>
/// TS-118 «Группы: создание с тримом и дубликат без учёта регистра»
/// (happy_path, FR-019, P0).
///
/// given: сессия teacher; группы ИК-224 не существует (демо-сид содержит только
///        ИК-221/222/223).
/// when:  POST /groups {name:'  ИК-224  '}; затем POST /groups {name:'ик-224'}.
/// then:  первый — 201 {name:'ИК-224', studentCount:0} (имя триммлено, счётчик
///        пустой группы); второй — 409 'Группа с таким названием уже
///        существует' (дубликат lower(name))
///        (AC FR-019 «Создание и дубликат»).
/// </summary>
public sealed class Ts118_GroupCreateTrimCiDuplicateTests : IClassFixture<B15GroupsDemoDataFactory>
{
    private const string ConflictText = "Группа с таким названием уже существует";

    private readonly B15GroupsDemoDataFactory _factory;

    public Ts118_GroupCreateTrimCiDuplicateTests(B15GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PostGroup_TrimsName_ThenCiDuplicateConflicts()
    {
        // given: сессия teacher; группы ИК-224 нет.
        using var client = B15GroupsSession.CreateTeacher(_factory);

        // when: POST /groups {name:'  ИК-224  '} — пробелы доходят до сервера
        // дословно (тело сериализуется как есть).
        using var created = await B15GroupsApi.PostGroupAsync(client, "  ИК-224  ");

        // then: 201 {name:'ИК-224', studentCount:0} — трим + счётчик пустой группы.
        using var createdBody = await B15GroupsApi.ParseWithStatusAsync(
            created, HttpStatusCode.Created, "POST /groups {name:'  ИК-224  '} (teacher)");
        Assert.Equal("ИК-224", B15GroupsApi.ReadString(createdBody.RootElement, "name"));
        Assert.Equal(0, B15GroupsApi.ReadInt(createdBody.RootElement, "studentCount"));

        // when: POST /groups {name:'ик-224'} — дубликат без учёта регистра.
        using var duplicate = await B15GroupsApi.PostGroupAsync(client, "ик-224");

        // then: 409 'Группа с таким названием уже существует'.
        using var duplicateBody = await B15GroupsApi.ParseWithStatusAsync(
            duplicate, HttpStatusCode.Conflict, "POST /groups {name:'ик-224'} (teacher)");
        B15GroupsApi.MessageIs(duplicateBody.RootElement, ConflictText);
    }
}
