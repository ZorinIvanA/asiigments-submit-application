using LabsApp.IntegrationTests.B14.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Groups.Scenarios;

/// <summary>
/// TS-106 «groups: состав с пагинацией; неизвестная группа — 404»
/// (happy_path, FR-019, P0).
///
/// given: в группе 25 студентов (демо-сид: ИК-221, student01..student25);
///        сессия teacher.
/// when:  GET /groups/{id}/students?page=3;
///        GET /groups/<несуществующий-uuid>/students.
/// then:  первый — 200 {items:5, total:25, page:3, pageSize:10}; все items —
///        StudentDto с groupId=id группы и groupName=её имя; порядок fullName↑
///        затем login↑ (ru); второй — 404 'Группа не найдена'
///        (FR-019 AC «Состав с пагинацией», «Неизвестная группа»).
/// </summary>
public sealed class Ts106_GroupRosterPaginationAndUnknownTests : IClassFixture<B14GroupsDemoDataFactory>
{
    // Сид: fullName 'Иванов Иван Иванович NN' различаются номерным суффиксом,
    // поэтому порядок fullName↑ затем login↑ (ru) кодируется логинами.
    private static readonly string[] Page3LoginsInOrder =
    [
        "student21", "student22", "student23", "student24", "student25",
    ];

    private const string NotFoundText = "Группа не найдена";

    private readonly B14GroupsDemoDataFactory _factory;

    public Ts106_GroupRosterPaginationAndUnknownTests(B14GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRosterPage3_ReturnsLastFiveStudentsOf25()
    {
        // given: сессия teacher; id группы с 25 студентами (ИК-221).
        using var client = B14GroupsSession.CreateTeacher(_factory);
        var ik221Id = await B14GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: GET /groups/{id}/students?page=3.
        using var response = await B14GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=3");

        // then: 200 {items:5, total:25, page:3, pageSize:10}.
        using var body = await B14GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=3 (teacher)");
        Assert.Equal(25, B14GroupsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(3, B14GroupsApi.ReadInt(body.RootElement, "page"));
        Assert.Equal(10, B14GroupsApi.ReadInt(body.RootElement, "pageSize"));

        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.True(
            items.Count == 5,
            $"Ожидалось 5 items на странице 3 из 25 студентов, фактически {items.Count}: {body.RootElement.GetRawText()}");

        // then: все items — StudentDto с groupId=id группы и groupName=её имя.
        foreach (var item in items)
        {
            Assert.Equal(ik221Id, B14GroupsApi.ReadString(item, "groupId"));
            Assert.Equal("ИК-221", B14GroupsApi.ReadString(item, "groupName"));
            // Состав StudentDto: {id, fullName, login, email, groupId, groupName}.
            B14GroupsApi.ReadString(item, "id");
            B14GroupsApi.ReadString(item, "fullName");
            B14GroupsApi.ReadString(item, "login");
            B14GroupsApi.ReadString(item, "email");
        }

        // then: порядок fullName↑ затем login↑ (ru): student21..student25.
        var logins = items.Select(item => B14GroupsApi.ReadString(item, "login")).ToArray();
        Assert.True(
            Page3LoginsInOrder.SequenceEqual(logins),
            $"Ожидался порядок состава [{string.Join(", ", Page3LoginsInOrder)}] " +
            $"(fullName↑ затем login↑, русская локаль), фактически [{string.Join(", ", logins)}].");
    }

    [Fact]
    public async Task GetRosterOfUnknownGroup_ReturnsNotFound()
    {
        // given: сессия teacher; группы с указанным uuid не существует.
        using var client = B14GroupsSession.CreateTeacher(_factory);
        var unknownId = Guid.NewGuid();

        // when: GET /groups/<несуществующий-uuid>/students.
        using var response = await B14GroupsApi.GetGroupStudentsAsync(client, unknownId.ToString());

        // then: 404 'Группа не найдена'.
        using var body = await B14GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.NotFound, $"GET /groups/{unknownId}/students (teacher)");
        B14GroupsApi.MessageIs(body.RootElement, NotFoundText);
    }
}
