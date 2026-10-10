using System.Text.Json;
using LabsApp.IntegrationTests.B15.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Groups.Scenarios;

/// <summary>
/// TS-122 «Группы: удаление сбрасывает groupId у студентов»
/// (happy_path, FR-019, FR-020, P0).
///
/// given: в ИК-222 есть 5 студентов (демо-сид); сессия teacher.
/// when:  DELETE /groups/{ИК-222}; затем GET /students?groupId=none.
/// then:  204; студенты ИК-222 живы с groupId=null — видны в выдаче
///        groupId=none; другие группы не затронуты
///        (AC FR-019 «Удаление сбрасывает группу у студентов»).
/// </summary>
public sealed class Ts122_GroupDeleteDetachesStudentsTests : IClassFixture<B15GroupsDemoDataFactory>
{
    private readonly B15GroupsDemoDataFactory _factory;

    public Ts122_GroupDeleteDetachesStudentsTests(B15GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task DeleteGroup_DetachesItsStudents_KeepsOtherGroups()
    {
        // given: сессия teacher; id группы ИК-222; в группе ровно 5 студентов
        // (состав читается из API — реализация given «в ИК-222 есть 5 студентов»).
        using var client = B15GroupsSession.CreateTeacher(_factory);
        var ik222Id = await B15GroupsApi.GetGroupIdByNameAsync(client, "ИК-222");
        var memberLogins = await GetRosterLoginsAsync(client, ik222Id);

        // when: DELETE /groups/{ИК-222}.
        using var deleted = await B15GroupsApi.DeleteGroupAsync(client, ik222Id);

        // then: 204 (пустое тело).
        Assert.True(
            deleted.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался HTTP 204 на шаге «DELETE /groups/{ik222Id}», фактически {(int)deleted.StatusCode}: " +
            await deleted.Content.ReadAsStringAsync());
        Assert.True(
            (await deleted.Content.ReadAsStringAsync()).Length == 0,
            "Ожидалось пустое тело ответа 204 на DELETE /groups/{id}.");

        // then: студенты ИК-222 живы с groupId=null — видны в выдаче
        // GET /students?groupId=none.
        using var nonePage = await B15GroupsApi.GetStudentsAsync(client, "groupId=none");
        using var noneBody = await B15GroupsApi.ParseWithStatusAsync(
            nonePage, HttpStatusCode.OK, "GET /students?groupId=none (после DELETE группы)");
        var items = noneBody.RootElement.GetProperty("items").EnumerateArray().ToList();

        foreach (var login in memberLogins)
        {
            JsonElement? detached = null;
            foreach (var item in items)
            {
                if (B15GroupsApi.ReadString(item, "login") == login)
                {
                    detached = item;
                    break;
                }
            }

            Assert.True(
                detached is not null,
                $"Ожидался отвязанный студент «{login}» в выдаче groupId=none, фактически его нет: " +
                $"{noneBody.RootElement.GetRawText()}");
            Assert.True(
                B15GroupsApi.ReadStringOrNull(detached!.Value, "groupId") is null,
                $"Ожидался groupId=null у отвязанного студента «{login}», фактически: {detached.Value.GetRawText()}");
        }

        // then: другие группы не затронуты — в списке остались ИК-221 (25) и
        // ИК-223 (0) с прежними счётчиками, ИК-222 отсутствует.
        using var groupsPage = await B15GroupsApi.GetGroupsAsync(client);
        using var groupsBody = await B15GroupsApi.ParseWithStatusAsync(
            groupsPage, HttpStatusCode.OK, "GET /groups (после DELETE ИК-222)");
        var remaining = groupsBody.RootElement.EnumerateArray()
            .Select(group => (Name: B15GroupsApi.ReadString(group, "name"), Count: B15GroupsApi.ReadInt(group, "studentCount")))
            .ToList();
        var expected = new List<(string Name, int Count)> { ("ИК-221", 25), ("ИК-223", 0) };
        Assert.True(
            remaining.SequenceEqual(expected),
            "Ожидался список групп [(ИК-221, 25), (ИК-223, 0)] после удаления ИК-222 " +
            "(другие группы не затронуты), фактически " +
            $"[{string.Join(", ", remaining.Select(item => $"({item.Name}, {item.Count})"))}].");
    }

    /// <summary>
    /// Читает состав группы (первая страница): given «в ИК-222 есть 5 студентов» —
    /// total и число items должны быть ровно 5.
    /// </summary>
    private static async Task<List<string>> GetRosterLoginsAsync(HttpClient client, string groupId)
    {
        using var response = await B15GroupsApi.GetGroupStudentsAsync(client, groupId);
        using var body = await B15GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{groupId}/students (given TS-122)");
        Assert.Equal(5, B15GroupsApi.ReadInt(body.RootElement, "total"));

        var logins = body.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => B15GroupsApi.ReadString(item, "login"))
            .ToList();
        Assert.True(
            logins.Count == 5,
            $"Ожидалось 5 студентов в составе ИК-222 (given TS-122), фактически {logins.Count}: {body.RootElement.GetRawText()}");
        return logins;
    }
}
