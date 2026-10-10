using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-199 «Группы: состав — некорректная page нормализуется к 1, страница
/// правее последней пуста» (boundary, FR-019, P1).
///
/// given: в ИК-221 25 студентов (демо-сид); сессия teacher.
/// when:  GET /api/v1/groups/{ИК-221.id}/students?page=0, затем ?page=abc, затем
///        ?page=2.5, затем ?page=99.
/// then:  первые три запроса — 200 с page=1 в ответе и непустой первой страницей
///        items (эхо некорректного значения запрещено); ?page=99 — 200
///        {items:[], total:25, page:99, pageSize:10} (FR-019: «page нормализуется
///        как в FR-017, страница правее последней — пустые items при корректном
///        total»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts199_GroupRosterPageNormalizationTests : IClassFixture<B16GroupsDemoDataFactory>
{
    private readonly B16GroupsDemoDataFactory _factory;

    public Ts199_GroupRosterPageNormalizationTests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=abc")]
    [InlineData("page=2.5")]
    public async Task GetRosterWithInvalidPage_NormalizesToPage1WithNonEmptyItems(string query)
    {
        // given: сессия teacher; id группы с 25 студентами (ИК-221).
        using var client = B16GroupsSession.CreateTeacher(_factory);
        var ik221Id = await B16GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: GET /groups/{id}/students?{query} (page=0 | page=abc | page=2.5).
        using var response = await B16GroupsApi.GetGroupStudentsAsync(client, ik221Id, query);

        // then: 200 с page=1 в ответе (эхо некорректного значения запрещено) и
        // непустой первой страницей items.
        using var body = await B16GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?{query} (teacher)");
        Assert.Equal(1, B16GroupsApi.ReadInt(body.RootElement, "page"));
        Assert.Equal(25, B16GroupsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(10, B16GroupsApi.ReadInt(body.RootElement, "pageSize"));
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.True(
            items.Count > 0,
            $"Ожидалась непустая первая страница items при некорректной page ({query}), " +
            $"фактически 0 записей: {body.RootElement.GetRawText()}");
    }

    [Fact]
    public async Task GetRosterWithPageBeyondLast_ReturnsEmptyItemsWithCorrectTotal()
    {
        // given: сессия teacher; id группы с 25 студентами (ИК-221).
        using var client = B16GroupsSession.CreateTeacher(_factory);
        var ik221Id = await B16GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: GET /groups/{id}/students?page=99.
        using var response = await B16GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=99");

        // then: 200 {items:[], total:25, page:99, pageSize:10}.
        using var body = await B16GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=99 (teacher)");
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.True(
            items.Count == 0,
            $"Ожидались пустые items на странице 99, фактически {items.Count}: {body.RootElement.GetRawText()}");
        Assert.Equal(25, B16GroupsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(99, B16GroupsApi.ReadInt(body.RootElement, "page"));
        Assert.Equal(10, B16GroupsApi.ReadInt(body.RootElement, "pageSize"));
    }
}
