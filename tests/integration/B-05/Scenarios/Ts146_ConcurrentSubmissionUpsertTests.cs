using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-146 «Конкурентный upsert сдачи по одной паре: без дублей»
/// (concurrency, FR-022, FR-002).
///
/// given: teacher авторизован; пара (student05, работа (1,3)) без записи
///        (демо-сид содержит сдачи только student01/student02); 20 параллельных
///        задач с cookie преподавателя.
/// when:  параллельно 20 PUT /api/v1/submissions с одинаковыми {studentId, labId,
///        submitDate:'2026-10-05', defenseDate:null}; затем GET
///        /api/v1/submissions?groupId={ИК-221}&semester=1&page=1.
/// then:  все 20 ответов — HTTP 200; в ведомости для пары (student05, (1,3))
///        ровно ОДНА запись (уникальность пары (studentId, labId) не нарушена
///        гонкой upsert). FR-022: «upsert по паре (studentId, labId)»;
///        relationship User-Submission: уникальность пары.
/// </summary>
public sealed class Ts146_ConcurrentSubmissionUpsertTests : IClassFixture<B05WebAppFactory>
{
    private const int ConcurrentRequests = 20;

    private readonly B05WebAppFactory _factory;

    public Ts146_ConcurrentSubmissionUpsertTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task TwentyConcurrentUpsertsOfSamePair_AllOkAndExactlyOneGridRecord()
    {
        // given: teacher авторизован; id student05 и id работы (сем.1, №3) из демо-сида.
        using var client = await HostClients.CreateTeacherClientAsync(_factory);

        var student05 = await FindStudentByLoginAsync(client, "student05");
        var labId = await FindLabIdAsync(client, semester: 1, number: 3);

        // when: параллельно 20 PUT /api/v1/submissions с одинаковым телом.
        var body = new
        {
            studentId = student05,
            labId,
            submitDate = "2026-10-05",
            defenseDate = (string?)null,
        };
        var putTasks = Enumerable.Range(0, ConcurrentRequests)
            .Select(_ => client.PutAsJsonAsync("/api/v1/submissions", body))
            .ToList();
        var responses = await Task.WhenAll(putTasks);

        // then: все 20 ответов — HTTP 200.
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        // затем: GET /api/v1/submissions?groupId={ИК-221}&semester=1&page=1 —
        // ровно ОДНА запись для пары (student05, работа (1,3)).
        var groupId = await FindGroupIdAsync(client, "ИК-221");
        using var grid = await client.GetAsync($"/api/v1/submissions?groupId={groupId}&semester=1&page=1");
        Assert.Equal(HttpStatusCode.OK, grid.StatusCode);

        var root = await BodyAssertions.ReadRootObjectAsync(grid);
        var submissions = root.GetProperty("submissions");
        Assert.Equal(JsonValueKind.Array, submissions.ValueKind);
        var pairRecords = submissions.EnumerateArray()
            .Where(record =>
                record.GetProperty("studentId").GetString() == student05 &&
                record.GetProperty("labId").GetString() == labId)
            .ToList();
        Assert.Single(pairRecords);
    }

    /// <summary>id студента по точному логину (search-токен — сам логин, FR-019).</summary>
    private static async Task<string> FindStudentByLoginAsync(HttpClient client, string login)
    {
        using var response = await client.GetAsync($"/api/v1/students?search={login}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        var items = root.GetProperty("items");
        var match = items.EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("login").GetString() == login);
        Assert.True(
            match.ValueKind == JsonValueKind.Object,
            $"Предусловие кейса: студент «{login}» не найден в демо-сиде.");
        return match.GetProperty("id").GetString()!;
    }

    /// <summary>id работы по паре (semester, number) из списка лабораторных (FR-013).</summary>
    private static async Task<string> FindLabIdAsync(HttpClient client, int semester, int number)
    {
        using var response = await client.GetAsync($"/api/v1/labs?semester={semester}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        var match = root.GetProperty("items").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("number").GetInt32() == number);
        Assert.True(
            match.ValueKind == JsonValueKind.Object,
            $"Предусловие кейса: работа ({semester},{number}) не найдена в демо-сиде.");
        return match.GetProperty("id").GetString()!;
    }

    /// <summary>id группы по названию (демо-сид FR-004: ИК-221/ИК-222/ИК-223).
    /// GET /api/v1/groups отвечает ГОЛЫМ массивом (FR-022) — тело разбирается
    /// как JSON-массив, без объектного утверждения ReadRootObjectAsync.</summary>
    private static async Task<string> FindGroupIdAsync(HttpClient client, string name)
    {
        using var response = await client.GetAsync("/api/v1/groups");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        var match = document.RootElement.EnumerateArray()
            .FirstOrDefault(group => group.GetProperty("name").GetString() == name);
        Assert.True(
            match.ValueKind == JsonValueKind.Object,
            $"Предусловие кейса: группа «{name}» не найдена в демо-сиде.");
        return match.GetProperty("id").GetString()!;
    }
}
