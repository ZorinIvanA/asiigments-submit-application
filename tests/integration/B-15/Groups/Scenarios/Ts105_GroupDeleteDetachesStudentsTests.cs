using System.Text.Json;
using LabsApp.IntegrationTests.B15.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Groups.Scenarios;

/// <summary>
/// TS-105 «groups: удаление сбрасывает groupId у студентов»
/// (data_integrity, FR-019, FR-020, P0).
///
/// given: в ИК-222 есть студенты (демо-сид: student26..student30); сессия teacher.
/// when:  DELETE /groups/{ИК-222.id}; затем GET /students?groupId=none и
///        повторный DELETE того же id.
/// then:  204; студенты живы с groupId=null и видны в выдаче groupId=none;
///        повторный DELETE — 404 'Группа не найдена'
///        (FR-019 AC «Удаление сбрасывает группу у студентов»).
/// </summary>
public sealed class Ts105_GroupDeleteDetachesStudentsTests : IClassFixture<B15GroupsDemoDataFactory>
{
    private static readonly string[] Ik222StudentLogins =
    [
        "student26", "student27", "student28", "student29", "student30",
    ];

    private const string NotFoundText = "Группа не найдена";

    private readonly B15GroupsDemoDataFactory _factory;

    public Ts105_GroupDeleteDetachesStudentsTests(B15GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task DeleteGroup_DetachesStudents_RepeatedDeleteNotFound()
    {
        // given: сессия teacher; id группы ИК-222 (в ней студенты student26..student30).
        using var client = B15GroupsSession.CreateTeacher(_factory);
        var ik222Id = await B15GroupsApi.GetGroupIdByNameAsync(client, "ИК-222");

        // when: DELETE /groups/{ИК-222.id}.
        using var deleted = await B15GroupsApi.DeleteGroupAsync(client, ik222Id);

        // then: 204 (пустое тело).
        Assert.True(
            deleted.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался HTTP 204 на шаге «DELETE /groups/{ik222Id}», фактически {(int)deleted.StatusCode}: " +
            await deleted.Content.ReadAsStringAsync());
        Assert.True(
            (await deleted.Content.ReadAsStringAsync()).Length == 0,
            "Ожидалось пустое тело ответа 204 на DELETE /groups/{id}.");

        // then: студенты живы с groupId=null и видны в выдаче GET /students?groupId=none.
        using var nonePage = await B15GroupsApi.GetStudentsAsync(client, "groupId=none");
        using var noneBody = await B15GroupsApi.ParseWithStatusAsync(
            nonePage, HttpStatusCode.OK, "GET /students?groupId=none (после DELETE группы)");
        var items = noneBody.RootElement.GetProperty("items").EnumerateArray().ToList();

        foreach (var login in Ik222StudentLogins)
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
            Assert.True(
                B15GroupsApi.ReadStringOrNull(detached.Value, "groupName") is null,
                $"Ожидался groupName=null у отвязанного студента «{login}», фактически: {detached.Value.GetRawText()}");
        }

        // when: повторный DELETE того же id.
        using var repeated = await B15GroupsApi.DeleteGroupAsync(client, ik222Id);

        // then: 404 'Группа не найдена'.
        using var repeatedBody = await B15GroupsApi.ParseWithStatusAsync(
            repeated, HttpStatusCode.NotFound, $"повторный DELETE /groups/{ik222Id} (teacher)");
        B15GroupsApi.MessageIs(repeatedBody.RootElement, NotFoundText);
    }
}
