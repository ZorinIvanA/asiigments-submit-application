using LabsApp.IntegrationTests.B08.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Groups.Scenarios;

/// <summary>
/// TS-167 «Атомарность уникальности: параллельные POST /labs одной пары»
/// (concurrency, FR-024, FR-017, P0).
///
/// given: пары (semester=1, number=99) не существует; сессия teacher.
/// when:  два параллельных POST /labs с парой (1,99).
/// then:  ровно один ответ 201, второй — 409 «Лабораторная с таким номером
///        уже есть в семестре» (CONFLICT_LAB); исключений нет (статусы 5xx
///        отсутствуют); в хранилище ровно одна запись пары (FR-024 AC
///        «Атомарность уникальности»).
/// </summary>
public sealed class Ts167_LabPairParallelCreateTests : IClassFixture<B08GroupsWebAppFactory>
{
    private const string LabPairJson =
        "{\"number\":99,\"semester\":1,\"content\":\"Параллельное создание пары\",\"assignmentUrl\":null,\"defenseRequired\":false}";

    private readonly B08GroupsWebAppFactory _factory;

    public Ts167_LabPairParallelCreateTests(B08GroupsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ParallelCreateSameLabPair_ExactlyOneCreatedOneConflict()
    {
        // given: teacher; пары (1,99) нет (хранилище без демо-набора).
        using var teacher = await B08GroupsClient.LoginAsTeacherAsync(_factory);

        // when: два параллельных POST /labs с парой (1,99).
        var firstTask = teacher.PostJsonAsync(B08GroupsClient.LabsEndpoint, LabPairJson);
        var secondTask = teacher.PostJsonAsync(B08GroupsClient.LabsEndpoint, LabPairJson);
        using var first = await firstTask;
        using var second = await secondTask;

        // then: ровно один 201, второй — 409; исключений нет (статусы 2xx/4xx/5xx проверены).
        var statuses = new[] { first.StatusCode, second.StatusCode };
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Conflict));
        Assert.DoesNotContain(statuses, status => (int)status >= 500);

        using var conflictBody = await B08GroupsClient.ReadJsonObjectAsync(
            first.StatusCode == HttpStatusCode.Conflict ? first : second,
            HttpStatusCode.Conflict,
            "проигравший параллельный POST /labs (TS-167)");
        Assert.Equal(
            B08GroupsClient.LabDuplicatePairMessage,
            B08GroupsClient.StringProperty(conflictBody.RootElement, "message"));

        // then: в хранилище ровно одна запись пары (1,99).
        using var stored = await teacher.GetAsync(B08GroupsClient.LabsEndpoint);
        using var storedBody = await B08GroupsClient.ReadJsonObjectAsync(
            stored, HttpStatusCode.OK, "GET /labs после гонки (TS-167)");
        var items = storedBody.RootElement.GetProperty("items");
        Assert.Equal(1, storedBody.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(99, items[0].GetProperty("number").GetInt32());
        Assert.Equal(1, items[0].GetProperty("semester").GetInt32());
    }
}
