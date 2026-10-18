using System.Text.Json;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-133 «Группы: удаление сбрасывает groupId у студентов»
/// (data_integrity, FR-019, FR-020, P0).
///
/// given: в ИК-222 есть 5 студентов (демо-сид student26..student30).
/// when:  DELETE /api/v1/groups/{ИК-222.id}; затем GET /students?groupId=none;
///        затем повторный GET /api/v1/groups.
/// then:  204; студенты живы с groupId=null и видимы в выдаче groupId=none
///        (в сиде +5 безгруппных: 2 сидовых безгруппных student31/32 + 5
///        отвязанных = total 7); при повторном GET /groups группы ИК-222 нет
///        (FR-019 AC «Удаление сбрасывает группу у студентов»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts133_GroupDeleteDetachesAndDropsGroupTests : IClassFixture<B16GroupsDemoDataFactory>
{
    /// <summary>Студенты ИК-222 демо-сида (сид: 26–30 → ИК-222).</summary>
    private static readonly string[] Ik222StudentLogins =
    [
        "student26", "student27", "student28", "student29", "student30",
    ];

    /// <summary>Сидовые безгруппные студенты (сид: 31–32 → без группы).</summary>
    private static readonly string[] SeedUngroupedStudentLogins = ["student31", "student32"];

    private readonly B16GroupsDemoDataFactory _factory;

    public Ts133_GroupDeleteDetachesAndDropsGroupTests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task DeleteGroup_DetachesStudents_VisibleInNoneFilter_GroupGone()
    {
        // given: сессия teacher; id группы ИК-222 (в ней 5 студентов демо-сида).
        using var client = B16GroupsSession.CreateTeacher(_factory);
        var ik222Id = await B16GroupsApi.GetGroupIdByNameAsync(client, "ИК-222");

        // when: DELETE /api/v1/groups/{ИК-222.id}.
        using var deleted = await B16GroupsApi.DeleteGroupAsync(client, ik222Id);

        // then: 204 (пустое тело).
        Assert.True(
            deleted.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался HTTP 204 на шаге «DELETE /groups/{ik222Id}», фактически {(int)deleted.StatusCode}: " +
            await deleted.Content.ReadAsStringAsync());
        Assert.True(
            (await deleted.Content.ReadAsStringAsync()).Length == 0,
            "Ожидалось пустое тело ответа 204 на DELETE /groups/{id}.");

        // when: GET /students?groupId=none.
        using var nonePage = await B16GroupsApi.GetStudentsAsync(client, "groupId=none");
        using var noneBody = await B16GroupsApi.ParseWithStatusAsync(
            nonePage, HttpStatusCode.OK, "GET /students?groupId=none (после DELETE группы)");
        var items = noneBody.RootElement.GetProperty("items").EnumerateArray().ToList();

        // then: студенты живы с groupId=null и видимы в выдаче groupId=none
        // (в сиде +5 безгруппных: student31/32 + 5 отвязанных = 7 записей).
        Assert.Equal(7, B16GroupsApi.ReadInt(noneBody.RootElement, "total"));
        var byLogin = new Dictionary<string, JsonElement>();
        foreach (var item in items)
        {
            byLogin[B16GroupsApi.ReadString(item, "login")] = item;
        }

        foreach (var login in Ik222StudentLogins)
        {
            Assert.True(
                byLogin.TryGetValue(login, out var detached),
                $"Ожидался отвязанный студент «{login}» в выдаче groupId=none, фактически его нет: " +
                $"{noneBody.RootElement.GetRawText()}");
            Assert.True(
                B16GroupsApi.ReadStringOrNull(detached, "groupId") is null,
                $"Ожидался groupId=null у отвязанного студента «{login}», фактически: {detached.GetRawText()}");
            Assert.True(
                B16GroupsApi.ReadStringOrNull(detached, "groupName") is null,
                $"Ожидался groupName=null у отвязанного студента «{login}», фактически: {detached.GetRawText()}");
        }

        foreach (var login in SeedUngroupedStudentLogins)
        {
            Assert.True(
                byLogin.TryGetValue(login, out var seedUngrouped),
                $"Ожидался сидовый безгруппный студент «{login}» в выдаче groupId=none, фактически его нет: " +
                $"{noneBody.RootElement.GetRawText()}");
            Assert.True(
                B16GroupsApi.ReadStringOrNull(seedUngrouped, "groupId") is null,
                $"Ожидался groupId=null у сидового безгруппного студента «{login}», фактически: {seedUngrouped.GetRawText()}");
        }

        // when: повторный GET /api/v1/groups.
        using var list = await B16GroupsApi.GetGroupsAsync(client);
        using var listBody = await B16GroupsApi.ParseWithStatusAsync(
            list, HttpStatusCode.OK, "GET /api/v1/groups (после DELETE группы)");
        var names = listBody.RootElement.EnumerateArray()
            .Select(group => B16GroupsApi.ReadString(group, "name"))
            .ToList();

        // then: группы ИК-222 нет; ИК-221 и ИК-223 сохранялись.
        Assert.True(
            names.SequenceEqual(["ИК-221", "ИК-223"]),
            $"Ожидался перечень групп [ИК-221, ИК-223] после удаления ИК-222, фактически [{string.Join(", ", names)}].");
    }
}
