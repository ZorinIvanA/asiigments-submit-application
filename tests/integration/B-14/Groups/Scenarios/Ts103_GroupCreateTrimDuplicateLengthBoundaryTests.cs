using LabsApp.IntegrationTests.B14.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Groups.Scenarios;

/// <summary>
/// TS-103 «groups: создание с тримом, дубликат без учёта регистра, границы
/// длины имени» (boundary, FR-019, P0).
///
/// given: сессия teacher; группы ИК-224 нет (демо-сид содержит только
///        ИК-221/222/223).
/// when:  POST /groups {name:'  ИК-224  '}; затем POST {name:'ик-224'};
///        отдельно POST с name длиной ровно 100 и длиной 101.
/// then:  первый — 201 {name:'ИК-224', studentCount:0}; второй —
///        409 'Группа с таким названием уже существует'; name длиной 100 — 201;
///        длиной 101 — 400 с errors.name=['Название группы — от 1 до 100
///        символов'] (FR-019 AC «Создание и дубликат»).
/// </summary>
public sealed class Ts103_GroupCreateTrimDuplicateLengthBoundaryTests : IClassFixture<B14GroupsDemoDataFactory>
{
    private const string ConflictText = "Группа с таким названием уже существует";
    private const string NameValidationText = "Название группы — от 1 до 100 символов";

    private readonly B14GroupsDemoDataFactory _factory;

    public Ts103_GroupCreateTrimDuplicateLengthBoundaryTests(B14GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PostGroup_TrimsName_ThenCiDuplicateConflicts()
    {
        // given: сессия teacher; группы ИК-224 нет.
        using var client = B14GroupsSession.CreateTeacher(_factory);

        // when: POST /groups {name:'  ИК-224  '}.
        using var created = await B14GroupsApi.PostGroupAsync(client, "  ИК-224  ");

        // then: 201 {name:'ИК-224', studentCount:0} — имя триммлено, счётчик пустой группы.
        using var createdBody = await B14GroupsApi.ParseWithStatusAsync(
            created, HttpStatusCode.Created, "POST /groups {name:'  ИК-224  '} (teacher)");
        Assert.Equal("ИК-224", B14GroupsApi.ReadString(createdBody.RootElement, "name"));
        Assert.Equal(0, B14GroupsApi.ReadInt(createdBody.RootElement, "studentCount"));

        // when: POST {name:'ик-224'} — дубликат без учёта регистра.
        using var duplicate = await B14GroupsApi.PostGroupAsync(client, "ик-224");

        // then: 409 'Группа с таким названием уже существует'.
        using var duplicateBody = await B14GroupsApi.ParseWithStatusAsync(
            duplicate, HttpStatusCode.Conflict, "POST /groups {name:'ик-224'} (teacher)");
        B14GroupsApi.MessageIs(duplicateBody.RootElement, ConflictText);
    }

    [Fact]
    public async Task PostGroup_NameOf100CharsCreated_NameOf101Rejected()
    {
        // given: сессия teacher; имена граничной длины без пробельных символов
        // (после трима длина сохраняется); в хранилище таких имён нет.
        using var client = B14GroupsSession.CreateTeacher(_factory);
        var name100 = new string('Г', 100);
        var name101 = new string('Г', 101);

        // when: POST с name длиной ровно 100.
        using var created = await B14GroupsApi.PostGroupAsync(client, name100);

        // then: 201.
        using var createdBody = await B14GroupsApi.ParseWithStatusAsync(
            created, HttpStatusCode.Created, "POST /groups {name:100 символов} (teacher)");
        Assert.Equal(name100, B14GroupsApi.ReadString(createdBody.RootElement, "name"));

        // when: POST с name длиной 101.
        using var rejected = await B14GroupsApi.PostGroupAsync(client, name101);

        // then: 400 с errors.name=['Название группы — от 1 до 100 символов'].
        using var rejectedBody = await B14GroupsApi.ParseWithStatusAsync(
            rejected, HttpStatusCode.BadRequest, "POST /groups {name:101 символ} (teacher)");
        B14GroupsApi.FieldErrorsExactly(rejectedBody.RootElement, "name", NameValidationText);
    }
}
