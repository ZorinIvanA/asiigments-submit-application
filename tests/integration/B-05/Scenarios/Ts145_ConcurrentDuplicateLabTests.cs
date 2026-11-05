using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-145 «Конкурентное создание дублирующей пары (semester, number)»
/// (concurrency, FR-014, FR-002).
///
/// given: teacher авторизован; пара (3,99) свободна (демо-сид содержит только
///        семестры 1 и 2 — в семестре 3 работ нет); два параллельных HTTP-клиента
///        с cookie преподавателя.
/// when:  одновременно (параллельные задачи) два POST /api/v1/labs с одинаковым
///        телом {number:99, semester:3, content:'X', assignmentUrl:null,
///        defenseRequired:false}; затем GET /api/v1/labs?semester=3.
/// then:  ровно один ответ — 201, второй — 409 «Лабораторная с таким номером
///        уже есть в семестре»; в списке semester=3 ровно одна работа с
///        number=99 (инвариант уникальности пары при гонке). FR-014:
///        уникальность (semester, number); FR-002: потокобезопасность.
/// </summary>
public sealed class Ts145_ConcurrentDuplicateLabTests : IClassFixture<B05WebAppFactory>
{
    private const string LabsEndpoint = "/api/v1/labs";

    private readonly B05WebAppFactory _factory;

    public Ts145_ConcurrentDuplicateLabTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task TwoConcurrentPostsOfSamePair_ExactlyOneCreatedAndOneConflict()
    {
        // given: teacher авторизован; два параллельных клиента с cookie преподавателя.
        using var clientA = await HostClients.CreateTeacherClientAsync(_factory);
        using var clientB = await HostClients.CreateTeacherClientAsync(_factory);

        // when: одновременно два POST /api/v1/labs с одинаковым телом {number:99, semester:3}.
        var body = new
        {
            number = 99,
            semester = 3,
            content = "X",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        };
        var firstTask = clientA.PostAsJsonAsync(LabsEndpoint, body);
        var secondTask = clientB.PostAsJsonAsync(LabsEndpoint, body);
        var responses = await Task.WhenAll(firstTask, secondTask);
        using (responses[0])
        using (responses[1])
        {
            // then: ровно один ответ — 201, второй — 409 с дословным текстом словаря.
            var statuses = responses.Select(response => response.StatusCode).OrderBy(status => status).ToList();
            Assert.Equal(
                new List<HttpStatusCode> { HttpStatusCode.Created, HttpStatusCode.Conflict },
                statuses);
            var conflict = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            var conflictBody = await BodyAssertions.ReadRootObjectAsync(conflict);
            BodyAssertions.MessageIs(conflictBody, "Лабораторная с таким номером уже есть в семестре");
        }

        // затем: GET /api/v1/labs?semester=3 — ровно одна работа с number=99.
        using var list = await clientA.GetAsync($"{LabsEndpoint}?semester=3");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(list);
        var items = root.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        var duplicates = items.EnumerateArray()
            .Where(item => item.GetProperty("number").GetInt32() == 99)
            .ToList();
        Assert.Single(duplicates);
    }
}
