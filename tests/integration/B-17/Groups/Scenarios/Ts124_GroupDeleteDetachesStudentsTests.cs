using System.Text.Json;
using LabsApp.IntegrationTests.B17.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Groups.Scenarios;

/// <summary>
/// TS-124 «Группы: удаление сбрасывает groupId у студентов» (data_integrity,
/// FR-019, FR-020, P0).
///
/// given: в ИК-222 есть студенты (демо-сид: student26..student30); клиент
///        анонимный (аутентификации в приложении нет — бывшие сессии teacher и
///        студента не существуют).
/// when:  DELETE /groups/{id ИК-222}; затем GET /students?groupId=none.
/// then:  204; студенты живы с groupId=null и groupName=null, видимы в выдаче
///        groupId=none (FR-019 AC «Удаление сбрасывает группу у студентов»).
/// </summary>
public sealed class Ts124_GroupDeleteDetachesStudentsTests : IClassFixture<B17GroupsDemoDataFactory>
{
    private static readonly string[] DetachedLogins =
    [
        "student26", "student27", "student28", "student29", "student30",
    ];

    private readonly B17GroupsDemoDataFactory _factory;

    public Ts124_GroupDeleteDetachesStudentsTests(B17GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task DeleteGroup_KeepsStudentsWithoutGroup()
    {
        // given: демо-сид (student26..student30 в ИК-222); клиент анонимный.
        using var client = B17GroupsSession.CreateClient(_factory);

        var ik222Id = await B17GroupsApi.GetGroupIdByNameAsync(client, "ИК-222");

        // when: DELETE /groups/{id ИК-222}.
        using var deleted = await B17GroupsApi.DeleteGroupAsync(client, ik222Id);

        // then: 204.
        Assert.True(
            deleted.StatusCode == HttpStatusCode.NoContent,
            $"DELETE /groups/{{ИК-222}} → ожидался 204, фактически {(int)deleted.StatusCode}: " +
            await deleted.Content.ReadAsStringAsync());

        // then: студенты 26–30 живы, видимы в GET /students?groupId=none, groupId/groupName — null.
        using var ungrouped = await B17GroupsApi.GetStudentsAsync(client, "groupId=none");
        using var ungroupedBody = await B17GroupsApi.ParseWithStatusAsync(
            ungrouped, HttpStatusCode.OK, "GET /students?groupId=none после удаления группы (TS-124)");
        var items = ungroupedBody.RootElement.GetProperty("items").EnumerateArray().ToList();
        var byLogin = items
            .Select(item => (Login: B17GroupsApi.ReadString(item, "login"), Item: item))
            .ToDictionary(pair => pair.Login, pair => pair.Item);

        foreach (var login in DetachedLogins)
        {
            Assert.True(
                byLogin.ContainsKey(login),
                $"Студент «{login}» из удалённой группы не найден в выдаче groupId=none.");
            Assert.Equal(JsonValueKind.Null, byLogin[login].GetProperty("groupId").ValueKind);
            Assert.Equal(JsonValueKind.Null, byLogin[login].GetProperty("groupName").ValueKind);
        }
    }
}
