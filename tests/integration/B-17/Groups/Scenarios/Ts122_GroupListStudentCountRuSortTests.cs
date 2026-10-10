using System.Text.Json;
using LabsApp.IntegrationTests.B17.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Groups.Scenarios;

/// <summary>
/// TS-122 «Группы: список со studentCount и ru-сортировкой» (happy_path, FR-019, P0).
///
/// given: сид — ИК-221 (25 студентов), ИК-222 (5), ИК-223 (0) (демо-сид
///        Seed__DemoData=true, воспроизводящий mock/seed.ts); сессия teacher.
/// when:  GET /api/v1/groups.
/// then:  200; массив из 3 GroupDto {id, name, studentCount} со значениями 25, 5, 0;
///        порядок по name без учёта регистра по правилам русской локали
///        (ИК-221, ИК-222, ИК-223)
///        (FR-019 AC «Список со счётчиком»; AR-005).
/// </summary>
public sealed class Ts122_GroupListStudentCountRuSortTests : IClassFixture<B17GroupsDemoDataFactory>
{
    private static readonly string[] ExpectedNamesInOrder = ["ИК-221", "ИК-222", "ИК-223"];
    private static readonly int[] ExpectedStudentCounts = [25, 5, 0];

    private readonly B17GroupsDemoDataFactory _factory;

    public Ts122_GroupListStudentCountRuSortTests(B17GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetGroups_ReturnsThreeGroupsWithCountsInRuNameOrder()
    {
        // given: сессия teacher.
        using var client = B17GroupsSession.CreateTeacher(_factory);

        // when: GET /api/v1/groups.
        using var response = await B17GroupsApi.GetGroupsAsync(client);

        // then: 200; массив из 3 GroupDto; studentCount 25, 5, 0; порядок ИК-221, ИК-222, ИК-223.
        using var body = await B17GroupsApi.ParseWithStatusAsync(
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
            var id = B17GroupsApi.ReadString(group, "id");
            Assert.True(
                Guid.TryParse(id, out _),
                $"Ожидался uuid в поле id GroupDto, фактически: «{id}»; тело: {group.GetRawText()}");
            names.Add(B17GroupsApi.ReadString(group, "name"));
            counts.Add(B17GroupsApi.ReadInt(group, "studentCount"));
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
