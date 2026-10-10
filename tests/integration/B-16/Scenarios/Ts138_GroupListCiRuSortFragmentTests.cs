using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-138 «Группы: сортировка имени без учёта регистра (ru) — единая культура
/// AR-005» (boundary, FR-019, P1).
///
/// given: созданы группы 'ик-225' и 'ИК-224' (плюс сид ИК-221..223); сессия
///        teacher.
/// when:  GET /groups.
/// then:  порядок фрагмента: 'ИК-224' раньше 'ик-225' (сравнение без учёта
///        регистра по правилам русской локали; FR-019/AR-005).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts138_GroupListCiRuSortFragmentTests : IClassFixture<B16GroupsDemoDataFactory>
{
    private readonly B16GroupsDemoDataFactory _factory;

    public Ts138_GroupListCiRuSortFragmentTests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetGroups_ListsCreatedLowercaseAndMixedCaseNamesInCiRuOrder()
    {
        // given: сессия teacher; созданы группы 'ик-225' и 'ИК-224' (сид
        // ИК-221..223 присутствует в фикстуре).
        using var client = B16GroupsSession.CreateTeacher(_factory);
        using var createdLower = await B16GroupsApi.PostGroupAsync(client, "ик-225");
        using var createdLowerBody = await B16GroupsApi.ParseWithStatusAsync(
            createdLower, HttpStatusCode.Created, "POST /groups {name:'ик-225'} (teacher)");
        Assert.Equal("ик-225", B16GroupsApi.ReadString(createdLowerBody.RootElement, "name"));

        using var createdMixed = await B16GroupsApi.PostGroupAsync(client, "ИК-224");
        using var createdMixedBody = await B16GroupsApi.ParseWithStatusAsync(
            createdMixed, HttpStatusCode.Created, "POST /groups {name:'ИК-224'} (teacher)");
        Assert.Equal("ИК-224", B16GroupsApi.ReadString(createdMixedBody.RootElement, "name"));

        // when: GET /groups.
        using var response = await B16GroupsApi.GetGroupsAsync(client);

        // then: 200; порядок фрагмента: 'ИК-224' раньше 'ик-225' (name↑ без учёта
        // регистра, русская локаль).
        using var body = await B16GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, "GET /api/v1/groups (teacher)");
        var names = body.RootElement.EnumerateArray()
            .Select(group => B16GroupsApi.ReadString(group, "name"))
            .ToList();

        Assert.True(
            names.Contains("ИК-224") && names.Contains("ик-225"),
            "Ожидалось присутствие созданных групп 'ИК-224' и 'ик-225' в списке, фактически " +
            $"[{string.Join(", ", names)}].");
        Assert.True(
            names.IndexOf("ИК-224") < names.IndexOf("ик-225"),
            "Ожидался порядок фрагмента 'ИК-224' раньше 'ик-225' (сравнение без учёта регистра " +
            $"по правилам русской локали), фактически [{string.Join(", ", names)}].");
    }
}
