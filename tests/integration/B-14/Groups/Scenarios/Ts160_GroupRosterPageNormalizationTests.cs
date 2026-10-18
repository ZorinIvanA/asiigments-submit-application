using LabsApp.IntegrationTests.B14.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Groups.Scenarios;

/// <summary>
/// TS-160 «groups/{id}/students: нормализация page и страница правее последней»
/// (boundary, FR-019, P1).
///
/// given: в группе 25 студентов (pageSize=10, полных страниц 3; демо-сид
///        ИК-221: student01..student25); сессия teacher.
/// when:  GET /api/v1/groups/{id}/students?page=0; ?page=abc; ?page=99.
/// then:  page=0 и page=abc — 200 с page=1 в ответе (эхо некорректного значения
///        запрещено; нормализация как в FR-017); page=99 — 200
///        {items:[], total:25, page:99, pageSize:10} (страница правее последней —
///        пустые items при корректном total) (FR-019).
/// </summary>
public sealed class Ts160_GroupRosterPageNormalizationTests : IClassFixture<B14GroupsDemoDataFactory>
{
    private readonly B14GroupsDemoDataFactory _factory;

    public Ts160_GroupRosterPageNormalizationTests(B14GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRosterWithPage0_NormalizesToPage1()
    {
        // given: сессия teacher; id группы с 25 студентами (ИК-221).
        using var client = B14GroupsSession.CreateTeacher(_factory);
        var ik221Id = await B14GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: GET /groups/{id}/students?page=0.
        using var response = await B14GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=0");

        // then: 200 с page=1 (нормализованное значение, эхо «0» запрещено).
        using var body = await B14GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=0 (teacher)");
        Assert.Equal(1, B14GroupsApi.ReadInt(body.RootElement, "page"));
        Assert.Equal(25, B14GroupsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(10, B14GroupsApi.ReadInt(body.RootElement, "pageSize"));
    }

    [Fact]
    public async Task GetRosterWithNonNumericPage_NormalizesToPage1()
    {
        // given: сессия teacher; id группы с 25 студентами (ИК-221).
        using var client = B14GroupsSession.CreateTeacher(_factory);
        var ik221Id = await B14GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: GET /groups/{id}/students?page=abc.
        using var response = await B14GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=abc");

        // then: 200 с page=1 (нормализованное значение, эхо «abc» запрещено).
        using var body = await B14GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=abc (teacher)");
        Assert.Equal(1, B14GroupsApi.ReadInt(body.RootElement, "page"));
        Assert.Equal(25, B14GroupsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(10, B14GroupsApi.ReadInt(body.RootElement, "pageSize"));
    }

    [Fact]
    public async Task GetRosterWithPageBeyondLast_ReturnsEmptyItemsWithCorrectTotal()
    {
        // given: сессия teacher; id группы с 25 студентами (ИК-221).
        using var client = B14GroupsSession.CreateTeacher(_factory);
        var ik221Id = await B14GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: GET /groups/{id}/students?page=99.
        using var response = await B14GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=99");

        // then: 200 {items:[], total:25, page:99, pageSize:10}.
        using var body = await B14GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=99 (teacher)");
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.True(
            items.Count == 0,
            $"Ожидались пустые items на странице 99, фактически {items.Count}: {body.RootElement.GetRawText()}");
        Assert.Equal(25, B14GroupsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(99, B14GroupsApi.ReadInt(body.RootElement, "page"));
        Assert.Equal(10, B14GroupsApi.ReadInt(body.RootElement, "pageSize"));
    }
}
