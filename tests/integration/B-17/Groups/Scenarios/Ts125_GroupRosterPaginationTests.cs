using LabsApp.IntegrationTests.B17.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Groups.Scenarios;

/// <summary>
/// TS-125 «Группы: состав с пагинацией и нормализацией page» (happy_path, FR-019, P0).
///
/// given: в группе ИК-221 25 студентов (демо-сид: student01..student25).
/// when:  GET /groups/{id}/students?page=3; затем GET /groups/{id}/students?page=abc.
/// then:  первый — 200 {items:5, total:25, page:3, pageSize:10}; все items имеют
///        groupId=id группы и groupName='ИК-221'; порядок fullName↑ затем login↑
///        (русская локаль; в сиде ФИО различаются только номерным суффиксом,
///        поэтому порядок кодируется логинами student21..student25);
///        второй — 200 с page=1 (нормализация page как в FR-017).
/// </summary>
public sealed class Ts125_GroupRosterPaginationTests : IClassFixture<B17GroupsDemoDataFactory>
{
    private static readonly string[] Page3LoginsInOrder =
    [
        "student21", "student22", "student23", "student24", "student25",
    ];

    private readonly B17GroupsDemoDataFactory _factory;

    public Ts125_GroupRosterPaginationTests(B17GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRosterPage3_ReturnsLastFiveStudentsOf25()
    {
        // given: сессия teacher; id группы ИК-221.
        using var client = B17GroupsSession.CreateTeacher(_factory);
        var ik221Id = await B17GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: GET /groups/{id}/students?page=3.
        using var response = await B17GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=3");

        // then: 200 {items:5, total:25, page:3, pageSize:10}.
        using var body = await B17GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=3 (teacher)");
        Assert.Equal(25, B17GroupsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(3, B17GroupsApi.ReadInt(body.RootElement, "page"));
        Assert.Equal(10, B17GroupsApi.ReadInt(body.RootElement, "pageSize"));

        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.True(
            items.Count == 5,
            $"Ожидалось 5 items на странице 3 из 25 студентов, фактически {items.Count}: {body.RootElement.GetRawText()}");

        // then: все items имеют groupId=id группы и groupName='ИК-221'.
        foreach (var item in items)
        {
            Assert.Equal(ik221Id, B17GroupsApi.ReadString(item, "groupId"));
            Assert.Equal("ИК-221", B17GroupsApi.ReadString(item, "groupName"));
        }

        // then: порядок fullName↑ затем login↑ (ru): student21..student25.
        var logins = items.Select(item => B17GroupsApi.ReadString(item, "login")).ToArray();
        Assert.True(
            Page3LoginsInOrder.SequenceEqual(logins),
            $"Ожидался порядок состава [{string.Join(", ", Page3LoginsInOrder)}] " +
            $"(fullName↑ затем login↑, русская локаль), фактически [{string.Join(", ", logins)}].");
    }

    [Fact]
    public async Task GetRosterWithNonNumericPage_NormalizesToPage1()
    {
        // given: сессия teacher; id группы ИК-221.
        using var client = B17GroupsSession.CreateTeacher(_factory);
        var ik221Id = await B17GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: GET /groups/{id}/students?page=abc.
        using var response = await B17GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=abc");

        // then: 200 с page=1 (нормализованное значение, не эхо входа).
        using var body = await B17GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=abc (teacher)");
        Assert.Equal(1, B17GroupsApi.ReadInt(body.RootElement, "page"));
    }
}
