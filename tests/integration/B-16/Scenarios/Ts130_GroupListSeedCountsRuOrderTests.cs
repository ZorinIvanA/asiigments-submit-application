using System.Text.Json;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-130 «Группы: список со studentCount и сортировкой по имени»
/// (happy_path, FR-019, P0).
///
/// given: демо-сид — ИК-221 (25 студентов), ИК-222 (5), ИК-223 (0); сессия
///        teacher (демо-сид Seed__DemoData=true воспроизводит данные кейса,
///        фикстура B16GroupsDemoDataFactory).
/// when:  GET /api/v1/groups.
/// then:  200 массив из 3 GroupDto с studentCount 25, 5, 0; порядок по имени без
///        учёта регистра (ru-локаль): ИК-221, ИК-222, ИК-223
///        (FR-019 AC «Список со счётчиком»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts130_GroupListSeedCountsRuOrderTests : IClassFixture<B16GroupsDemoDataFactory>
{
    /// <summary>Ожидаемый порядок name↑ (ru, IgnoreCase) демо-сида.</summary>
    private static readonly string[] ExpectedNamesInOrder = ["ИК-221", "ИК-222", "ИК-223"];

    /// <summary>Ожидаемые studentCount в том же порядке (данные given).</summary>
    private static readonly int[] ExpectedStudentCounts = [25, 5, 0];

    private readonly B16GroupsDemoDataFactory _factory;

    public Ts130_GroupListSeedCountsRuOrderTests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetGroups_ReturnsThreeSeedGroupsWithCountsInRuNameOrder()
    {
        // given: сессия teacher; демо-сид ИК-221 (25), ИК-222 (5), ИК-223 (0).
        using var client = B16GroupsSession.CreateTeacher(_factory);

        // when: GET /api/v1/groups.
        using var response = await B16GroupsApi.GetGroupsAsync(client);

        // then: 200 массив из 3 GroupDto {id, name, studentCount}.
        using var body = await B16GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, "GET /api/v1/groups (teacher)");
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);

        var groups = body.RootElement.EnumerateArray().ToList();
        Assert.True(
            groups.Count == 3,
            $"Ожидалось 3 группы демо-сида в списке, фактически {groups.Count}: {body.RootElement.GetRawText()}");

        var names = new List<string>(3);
        var counts = new List<int>(3);
        foreach (var group in groups)
        {
            // Состав GroupDto: {id, name, studentCount}.
            var id = B16GroupsApi.ReadString(group, "id");
            Assert.True(
                Guid.TryParse(id, out _),
                $"Ожидался uuid в поле id GroupDto, фактически: «{id}»; тело: {group.GetRawText()}");
            names.Add(B16GroupsApi.ReadString(group, "name"));
            counts.Add(B16GroupsApi.ReadInt(group, "studentCount"));
        }

        // then: порядок по имени без учёта регистра (ru-локаль): ИК-221, ИК-222, ИК-223.
        Assert.True(
            ExpectedNamesInOrder.SequenceEqual(names),
            $"Ожидался порядок групп [{string.Join(", ", ExpectedNamesInOrder)}] " +
            $"(name↑ без учёта регистра, русская локаль), фактически [{string.Join(", ", names)}].");

        // then: studentCount 25, 5, 0 (вычисляется по текущему состоянию, IF-010).
        Assert.True(
            ExpectedStudentCounts.SequenceEqual(counts),
            $"Ожидались studentCount [{string.Join(", ", ExpectedStudentCounts)}], " +
            $"фактически [{string.Join(", ", counts)}].");
    }
}
