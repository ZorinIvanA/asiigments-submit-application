using System.Text.Json;
using LabsApp.IntegrationTests.B15.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Groups.Scenarios;

/// <summary>
/// TS-102 «groups: список со studentCount и сортировкой имён (ru)»
/// (happy_path, FR-019, P0).
///
/// given: группы ИК-221 (25 студентов), ИК-222 (5), ИК-223 (0) (демо-сид
///        Seed__DemoData=true); сессия teacher.
/// when:  GET /api/v1/groups.
/// then:  200 массив из 3 GroupDto {id, name, studentCount} со значениями 25, 5, 0;
///        порядок имён по возрастанию без учёта регистра по правилам русской
///        локали (ИК-221, ИК-222, ИК-223)
///        (FR-019 AC «Список со счётчиком», AR-005).
/// </summary>
public sealed class Ts102_GroupListStudentCountRuSortTests : IClassFixture<B15GroupsDemoDataFactory>
{
    private static readonly string[] ExpectedNamesInOrder = ["ИК-221", "ИК-222", "ИК-223"];
    private static readonly int[] ExpectedStudentCounts = [25, 5, 0];

    private readonly B15GroupsDemoDataFactory _factory;

    public Ts102_GroupListStudentCountRuSortTests(B15GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetGroups_ReturnsThreeGroupsWithCountsInRuNameOrder()
    {
        // given: сессия teacher.
        using var client = B15GroupsSession.CreateTeacher(_factory);

        // when: GET /api/v1/groups.
        using var response = await B15GroupsApi.GetGroupsAsync(client);

        // then: 200; массив из 3 GroupDto; studentCount 25, 5, 0; порядок ИК-221, ИК-222, ИК-223.
        using var body = await B15GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, "GET /api/v1/groups (teacher)");
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);

        var groups = body.RootElement.EnumerateArray().ToList();
        Assert.True(
            groups.Count == 3,
            $"Ожидалось 3 группы в списке, фактически {groups.Count}: {body.RootElement.GetRawText()}");

        var names = new List<string>(3);
        var counts = new List<int>(3);
        foreach (var group in groups)
        {
            // Состав GroupDto: {id, name, studentCount}.
            var id = B15GroupsApi.ReadString(group, "id");
            Assert.True(
                Guid.TryParse(id, out _),
                $"Ожидался uuid в поле id GroupDto, фактически: «{id}»; тело: {group.GetRawText()}");
            names.Add(B15GroupsApi.ReadString(group, "name"));
            counts.Add(B15GroupsApi.ReadInt(group, "studentCount"));
        }

        Assert.True(
            ExpectedNamesInOrder.SequenceEqual(names),
            $"Ожидался порядок групп [{string.Join(", ", ExpectedNamesInOrder)}] " +
            $"(name↑ без учёта регистра, русская локаль), фактически [{string.Join(", ", names)}].");
        Assert.True(
            ExpectedStudentCounts.SequenceEqual(counts),
            $"Ожидались studentCount [25, 5, 0], фактически [{string.Join(", ", counts)}].");
    }
}
