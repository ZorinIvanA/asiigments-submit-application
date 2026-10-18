using System.Text.Json;
using LabsApp.IntegrationTests.B17.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Groups.Scenarios;

/// <summary>
/// TS-123 «Группы: создание с тримом и дубликат без учёта регистра»
/// (happy_path, FR-019, P0).
///
/// given: сессия teacher; группы ИК-224 не существует (демо-сид содержит только
///        ИК-221/222/223).
/// when:  POST /groups {name:'  ИК-224  '}; затем POST /groups {name:'ик-224'}.
/// then:  первый — 201 {name:'ИК-224', studentCount:0}; второй — 409 «Группа с
///        таким названием уже существует» (FR-019 AC «Создание и дубликат»).
/// </summary>
public sealed class Ts123_GroupCreateTrimAndDuplicateTests : IClassFixture<B17GroupsDemoDataFactory>
{
    private readonly B17GroupsDemoDataFactory _factory;

    public Ts123_GroupCreateTrimAndDuplicateTests(B17GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PostGroupTrimsName_ThenCiDuplicateReturns409()
    {
        // given: сессия teacher.
        using var client = B17GroupsSession.CreateTeacher(_factory);

        // when: POST /groups {name:'  ИК-224  '}.
        using var created = await B17GroupsApi.PostGroupAsync(client, "  ИК-224  ");

        // then: 201 {name:'ИК-224', studentCount:0} (имя триммлено).
        using var createdBody = await B17GroupsApi.ParseWithStatusAsync(
            created, HttpStatusCode.Created, "POST /groups {name:'  ИК-224  '} (teacher)");
        Assert.Equal("ИК-224", B17GroupsApi.ReadString(createdBody.RootElement, "name"));
        Assert.Equal(0, B17GroupsApi.ReadInt(createdBody.RootElement, "studentCount"));
        Assert.True(
            Guid.TryParse(B17GroupsApi.ReadString(createdBody.RootElement, "id"), out _),
            $"Ожидался uuid в поле id созданной GroupDto: {createdBody.RootElement.GetRawText()}");

        // when: POST /groups {name:'ик-224'} — дубликат в другом регистре.
        using var duplicate = await B17GroupsApi.PostGroupAsync(client, "ик-224");

        // then: 409 «Группа с таким названием уже существует».
        using var duplicateBody = await B17GroupsApi.ParseWithStatusAsync(
            duplicate, HttpStatusCode.Conflict, "POST /groups {name:'ик-224'} (дубликат ci)");
        B17GroupsApi.MessageIs(duplicateBody.RootElement, "Группа с таким названием уже существует");
    }
}
