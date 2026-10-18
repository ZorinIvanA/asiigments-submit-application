using LabsApp.IntegrationTests.B15.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Groups.Scenarios;

/// <summary>
/// TS-125 «Группы: нормализация page в составе» (boundary, FR-019, P1).
///
/// given: в группе 25 студентов (демо-сид: ИК-221, pageSize=10); сессия teacher.
/// when:  GET /groups/{id}/students?page=abc; затем ?page=99.
/// then:  для 'abc' — page=1 в ответе (нормализация как в FR-017, эхо
///        некорректного значения запрещено); для 99 — items=[], total=25,
///        page=99 (страница правее последней — пустые items при корректном
///        total) (FR-019).
/// </summary>
public sealed class Ts125_GroupRosterPageNormalizationTests : IClassFixture<B15GroupsDemoDataFactory>
{
    private readonly B15GroupsDemoDataFactory _factory;

    public Ts125_GroupRosterPageNormalizationTests(B15GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRosterWithNonNumericPage_NormalizesToPage1()
    {
        // given: сессия teacher; id группы с 25 студентами (ИК-221).
        using var client = B15GroupsSession.CreateTeacher(_factory);
        var ik221Id = await B15GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: GET /groups/{id}/students?page=abc.
        using var response = await B15GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=abc");

        // then: page=1 в ответе (нормализованное значение, эхо «abc» запрещено).
        using var body = await B15GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=abc (teacher)");
        Assert.Equal(1, B15GroupsApi.ReadInt(body.RootElement, "page"));
    }

    [Fact]
    public async Task GetRosterWithPageBeyondLast_ReturnsEmptyItemsWithCorrectTotal()
    {
        // given: сессия teacher; id группы с 25 студентами (ИК-221).
        using var client = B15GroupsSession.CreateTeacher(_factory);
        var ik221Id = await B15GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: GET /groups/{id}/students?page=99.
        using var response = await B15GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=99");

        // then: 200 {items:[], total:25, page:99}.
        using var body = await B15GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=99 (teacher)");
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.True(
            items.Count == 0,
            $"Ожидались пустые items на странице 99, фактически {items.Count}: {body.RootElement.GetRawText()}");
        Assert.Equal(25, B15GroupsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(99, B15GroupsApi.ReadInt(body.RootElement, "page"));
    }
}
