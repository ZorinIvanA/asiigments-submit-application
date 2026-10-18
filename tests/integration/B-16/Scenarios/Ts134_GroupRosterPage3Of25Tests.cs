using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-134 «Группы: состав с пагинацией (страница 3 из 25)»
/// (happy_path, FR-019, P0).
///
/// given: в ИК-221 25 студентов (демо-сид student01..student25).
/// when:  GET /api/v1/groups/{ИК-221.id}/students?page=3.
/// then:  200 {items:5, total:25, page:3, pageSize:10}; все items — StudentDto с
///        groupId=id группы и groupName='ИК-221' (FR-019 AC «Состав с
///        пагинацией»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts134_GroupRosterPage3Of25Tests : IClassFixture<B16GroupsDemoDataFactory>
{
    private readonly B16GroupsDemoDataFactory _factory;

    public Ts134_GroupRosterPage3Of25Tests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRosterPage3_ReturnsTailOfFiveWithGroupFields()
    {
        // given: сессия teacher; id группы ИК-221 (25 сид-студентов).
        using var client = B16GroupsSession.CreateTeacher(_factory);
        var ik221Id = await B16GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: GET /api/v1/groups/{ИК-221.id}/students?page=3.
        using var response = await B16GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=3");

        // then: 200 {items:5, total:25, page:3, pageSize:10}.
        using var body = await B16GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=3 (teacher)");
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(25, B16GroupsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(3, B16GroupsApi.ReadInt(body.RootElement, "page"));
        Assert.Equal(10, B16GroupsApi.ReadInt(body.RootElement, "pageSize"));
        Assert.True(
            items.Count == 5,
            $"Ожидалось 5 items на странице 3 из 25 студентов, фактически {items.Count}: " +
            $"{body.RootElement.GetRawText()}");

        // then: все items — StudentDto с groupId=id группы и groupName='ИК-221'.
        foreach (var item in items)
        {
            Assert.Equal(ik221Id, B16GroupsApi.ReadString(item, "groupId"));
            Assert.Equal("ИК-221", B16GroupsApi.ReadString(item, "groupName"));
            // Состав StudentDto: {id, fullName, login, email, groupId, groupName}.
            Assert.True(
                Guid.TryParse(B16GroupsApi.ReadString(item, "id"), out _),
                $"Ожидался uuid в поле id StudentDto: {item.GetRawText()}");
            B16GroupsApi.ReadString(item, "fullName");
            B16GroupsApi.ReadString(item, "login");
            B16GroupsApi.ReadString(item, "email");
        }
    }
}
