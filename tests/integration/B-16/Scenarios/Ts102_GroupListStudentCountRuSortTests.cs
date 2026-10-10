using System.Text.Json;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-102 «groups: список со studentCount и сортировкой имён (ru,
/// дискриминирующие данные)» (happy_path, FR-019, P0; РЕВ-ISS-003).
///
/// given: группы ИК-221 (25 студентов), ИК-222 (5), ИК-223 (0) (демо-сид
///        Seed__DemoData=true); сессия teacher дополнительно создала через
///        POST /api/v1/groups две группы с именами, дающими РАЗНЫЙ порядок при
///        ru-культуре и ординальном сравнении в любом регистре: «Аврора» (А) и
///        «Ёлка» (Ё) — при ru: Аврора &lt; Ёлка &lt; ИК-*; ординал по нижнему регистру:
///        ё (U+0451) ПОСЛЕ и (U+0438) — Ёлка оказалась бы последней; ординал по
///        верхнему регистру: Ё (U+0401) ДО а/А — Ёлка оказалась бы первой
///        (данные делают утверждение о ru-локали фальсифицируемым).
/// when:  GET /api/v1/groups.
/// then:  200 массив из 5 GroupDto {id, name, studentCount}; studentCount:
///        ИК-221=25, ИК-222=5, ИК-223=0, Аврора=0, Ёлка=0; порядок имён по
///        возрастанию без учёта регистра ПО ПРАВИЛАМ РУССКОЙ ЛОКАЛИ (ru) — в
///        точности ['Аврора','Ёлка','ИК-221','ИК-222','ИК-223'] (единая культура
///        сравнения со списками студентов — AR-005); любой иной порядок
///        (например, Ёлка первой или последней) — провал кейса
///        (FR-019 AC «Список со счётчиком»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts102_GroupListStudentCountRuSortTests : IClassFixture<B16GroupsDemoDataFactory>
{
    /// <summary>Ожидаемый порядок name↑ (ru, IgnoreCase): А &lt; Ё &lt; И.</summary>
    private static readonly string[] ExpectedNamesInOrder = ["Аврора", "Ёлка", "ИК-221", "ИК-222", "ИК-223"];

    /// <summary>Ожидаемые studentCount в том же порядке (Рев-ISS-003 данные).</summary>
    private static readonly int[] ExpectedStudentCounts = [0, 0, 25, 5, 0];

    private readonly B16GroupsDemoDataFactory _factory;

    public Ts102_GroupListStudentCountRuSortTests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetGroups_ReturnsFiveGroupsWithCountsInRuNameOrder()
    {
        // given: сессия teacher; группы ИК-221/222/223 демо-сида.
        using var client = B16GroupsSession.CreateTeacher(_factory);

        // given: teacher через POST /api/v1/groups создаёт «Аврора» и «Ёлка».
        using var avrora = await B16GroupsApi.PostGroupAsync(client, "Аврора");
        using var avroraBody = await B16GroupsApi.ParseWithStatusAsync(
            avrora, HttpStatusCode.Created, "POST /groups {name:'Аврора'} (teacher)");
        Assert.Equal("Аврора", B16GroupsApi.ReadString(avroraBody.RootElement, "name"));
        Assert.Equal(0, B16GroupsApi.ReadInt(avroraBody.RootElement, "studentCount"));

        using var yolka = await B16GroupsApi.PostGroupAsync(client, "Ёлка");
        using var yolkaBody = await B16GroupsApi.ParseWithStatusAsync(
            yolka, HttpStatusCode.Created, "POST /groups {name:'Ёлка'} (teacher)");
        Assert.Equal("Ёлка", B16GroupsApi.ReadString(yolkaBody.RootElement, "name"));

        // when: GET /api/v1/groups.
        using var response = await B16GroupsApi.GetGroupsAsync(client);

        // then: 200; массив из 5 GroupDto {id, name, studentCount}.
        using var body = await B16GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.OK, "GET /api/v1/groups (teacher)");
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);

        var groups = body.RootElement.EnumerateArray().ToList();
        Assert.True(
            groups.Count == 5,
            $"Ожидалось 5 групп в списке, фактически {groups.Count}: {body.RootElement.GetRawText()}");

        var names = new List<string>(5);
        var counts = new List<int>(5);
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

        // then: порядок в точности ['Аврора','Ёлка','ИК-221','ИК-222','ИК-223']
        // (ru-локаль, без учёта регистра); Ёлка первой (ordinal-upper) или
        // последней (ordinal-lower) — провал кейса.
        Assert.True(
            ExpectedNamesInOrder.SequenceEqual(names),
            $"Ожидался порядок групп [{string.Join(", ", ExpectedNamesInOrder)}] " +
            $"(name↑ без учёта регистра, русская локаль), фактически [{string.Join(", ", names)}].");

        // then: studentCount ИК-221=25, ИК-222=5, ИК-223=0, Аврора=0, Ёлка=0.
        Assert.True(
            ExpectedStudentCounts.SequenceEqual(counts),
            $"Ожидались studentCount [{string.Join(", ", ExpectedStudentCounts)}], " +
            $"фактически [{string.Join(", ", counts)}].");
    }
}
