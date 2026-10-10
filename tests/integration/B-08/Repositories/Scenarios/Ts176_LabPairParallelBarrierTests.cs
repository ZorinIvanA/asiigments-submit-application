using LabsApp.IntegrationTests.B08.Repositories.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Repositories.Scenarios;

/// <summary>
/// TS-176 «Атомарность уникальности: параллельные POST /labs с одной парой»
/// (concurrency, FR-024, FR-017, P0).
///
/// given: пара (semester=5, number=1) свободна; два параллельных запроса с
///        барьерой старта; сессии teacher у обоих (клиент на поток —
///        независимые cookie-контейнеры).
/// when:  два одновременных POST /labs {number:1, semester:5, …} — потоки
///        синхронизируются Barrier(2) непосредственно перед отправкой.
/// then:  ровно один ответ 201, второй — 409; без исключений (статусов 5xx
///        нет); в хранилище ровно одна запись пары (FR-024 AC «Атомарность
///        уникальности»).
/// </summary>
public sealed class Ts176_LabPairParallelBarrierTests : IClassFixture<B08RepositoriesWebAppFactory>
{
    private const string LabPairJson =
        "{\"number\":1,\"semester\":5,\"content\":\"Параллельное создание пары (5,1)\"," +
        "\"assignmentUrl\":null,\"defenseRequired\":false}";

    private readonly B08RepositoriesWebAppFactory _factory;

    public Ts176_LabPairParallelBarrierTests(B08RepositoriesWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ParallelCreateSameLabPair_ExactlyOneCreatedOneConflict()
    {
        // given: пара (5,1) свободна; сессии teacher у обоих участников.
        using var firstTeacher = await B08RepositoriesClient.LoginAsTeacherAsync(_factory);
        using var secondTeacher = await B08RepositoriesClient.LoginAsTeacherAsync(_factory);

        // when: два одновременных POST /labs с барьерой старта.
        using var barrier = new Barrier(participantCount: 2);
        var firstTask = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await firstTeacher.PostJsonAsync(B08RepositoriesClient.LabsEndpoint, LabPairJson);
        });
        var secondTask = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await secondTeacher.PostJsonAsync(B08RepositoriesClient.LabsEndpoint, LabPairJson);
        });
        using var first = await firstTask;
        using var second = await secondTask;

        // then: ровно один 201, второй — 409; без исключений (5xx нет).
        var statuses = new[] { first.StatusCode, second.StatusCode };
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Conflict));
        Assert.DoesNotContain(statuses, status => (int)status >= 500);

        // then: в хранилище ровно одна запись пары (semester=5, number=1).
        using var stored = await firstTeacher.GetAsync($"{B08RepositoriesClient.LabsEndpoint}?semester=5");
        using var storedBody = await B08RepositoriesClient.ReadJsonObjectAsync(
            stored, HttpStatusCode.OK, "GET /labs?semester=5 после гонки (TS-176)");
        var items = storedBody.RootElement.GetProperty("items");
        Assert.Equal(1, storedBody.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(1, items[0].GetProperty("number").GetInt32());
        Assert.Equal(5, items[0].GetProperty("semester").GetInt32());
    }
}
