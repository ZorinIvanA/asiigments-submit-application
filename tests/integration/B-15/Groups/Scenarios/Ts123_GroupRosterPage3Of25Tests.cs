using LabsApp.IntegrationTests.B15.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Groups.Scenarios;

/// <summary>
/// TS-123 «Группы: состав с пагинацией (страница 3 из 25)»
/// (happy_path, FR-019, P0).
///
/// given: в группе ИК-221 25 студентов (демо-сид: student01..student25); сессия
///        teacher.
/// when:  GET /groups/{id ИК-221}/students?page=3.
/// then:  200 {items:5, total:25, page:3, pageSize:10}; все items — StudentDto
///        с groupId=id группы и groupName='ИК-221'; порядок fullName↑ затем
///        login↑ (русская локаль) (AC FR-019 «Состав с пагинацией»).
/// </summary>
public sealed class Ts123_GroupRosterPage3Of25Tests : IClassFixture<B15GroupsDemoDataFactory>
{
    // Сид: fullName 'Иванов Иван Иванович NN' различаются номерным суффиксом,
    // поэтому порядок fullName↑ затем login↑ (ru) кодируется логинами.
    private static readonly string[] Page3LoginsInOrder =
    [
        "student21", "student22", "student23", "student24", "student25",
    ];

    private readonly B15GroupsDemoDataFactory _factory;

    public Ts123_GroupRosterPage3Of25Tests(B15GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRosterPage3_ReturnsLastFiveStudentsOf25()
    {
        // given: сессия teacher; id группы ИК-221 (в ней 25 студентов — демо-сид).
        using var client = B15GroupsSession.CreateTeacher(_factory);
        var ik221Id = await B15GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: GET /groups/{id ИК-221}/students?page=3.
        using var response = await B15GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=3");

        // then: 200 {items:5, total:25, page:3, pageSize:10}.
        using var body = await B15GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=3 (teacher)");
        Assert.Equal(25, B15GroupsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(3, B15GroupsApi.ReadInt(body.RootElement, "page"));
        Assert.Equal(10, B15GroupsApi.ReadInt(body.RootElement, "pageSize"));

        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.True(
            items.Count == 5,
            $"Ожидалось 5 items на странице 3 из 25 студентов, фактически {items.Count}: {body.RootElement.GetRawText()}");

        // then: все items — StudentDto с groupId=id группы и groupName='ИК-221'.
        foreach (var item in items)
        {
            Assert.Equal(ik221Id, B15GroupsApi.ReadString(item, "groupId"));
            Assert.Equal("ИК-221", B15GroupsApi.ReadString(item, "groupName"));
            // Состав StudentDto: {id, fullName, login, email, groupId, groupName}.
            B15GroupsApi.ReadString(item, "id");
            B15GroupsApi.ReadString(item, "fullName");
            B15GroupsApi.ReadString(item, "login");
            B15GroupsApi.ReadString(item, "email");
        }

        // then: порядок fullName↑ затем login↑ (русская локаль): student21..student25.
        var logins = items.Select(item => B15GroupsApi.ReadString(item, "login")).ToArray();
        Assert.True(
            Page3LoginsInOrder.SequenceEqual(logins),
            $"Ожидался порядок состава [{string.Join(", ", Page3LoginsInOrder)}] " +
            $"(fullName↑ затем login↑, русская локаль), фактически [{string.Join(", ", logins)}].");
    }
}
