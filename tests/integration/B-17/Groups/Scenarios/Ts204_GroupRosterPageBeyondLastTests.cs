using System.Text.Json;
using LabsApp.IntegrationTests.B17.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Groups.Scenarios;

/// <summary>
/// TS-204 «Состав группы: страница правее последней» (boundary, FR-019, P1).
///
/// given: в группе ИК-221 25 студентов при pageSize=10 (демо-сид); сессия teacher.
/// when:  GET /groups/{id ИК-221}/students?page=99.
/// then:  200; items=[]; total=25; page=99; pageSize=10 — пустые items при
///        корректном total (FR-019: «page нормализуется как в FR-017 … страница
///        правее последней — пустые items при корректном total»).
/// </summary>
public sealed class Ts204_GroupRosterPageBeyondLastTests : IClassFixture<B17GroupsDemoDataFactory>
{
    private readonly B17GroupsDemoDataFactory _factory;

    public Ts204_GroupRosterPageBeyondLastTests(B17GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRosterPageBeyondLast_ReturnsEmptyItemsWithCorrectTotal()
    {
        // given: сессия teacher; id группы ИК-221 (25 студентов).
        using var client = B17GroupsSession.CreateTeacher(_factory);
        var ik221Id = await B17GroupsApi.GetGroupIdByNameAsync(client, "ИК-221");

        // when: GET /groups/{id}/students?page=99.
        using var response = await B17GroupsApi.GetGroupStudentsAsync(client, ik221Id, "page=99");

        // then: 200; items=[]; total=25; page=99; pageSize=10.
        using var body = await B17GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /groups/{ik221Id}/students?page=99 (teacher)");
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("items").ValueKind);
        Assert.Empty(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(25, B17GroupsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(99, B17GroupsApi.ReadInt(body.RootElement, "page"));
        Assert.Equal(10, B17GroupsApi.ReadInt(body.RootElement, "pageSize"));
    }
}
