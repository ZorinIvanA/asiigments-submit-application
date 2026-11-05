using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-089 «labs: фильтр semester и сортировка number desc» (FR-017 AC «Фильтр
/// и сортировка»; P0): given — тот же сид (23 работы); клиент анонимный; when — GET
/// /labs?semester=2&amp;sortField=number&amp;sortDir=desc; then — 200; total=3;
/// items упорядочены по number 3,2,1 (вторичный ключ semester asc не влияет
/// в пределах одного семестра).
/// </summary>
public sealed class Ts089_LabsSemesterFilterNumberDescTests : IClassFixture<B03HostFactory>,
    IAsyncLifetime
{
    private readonly B03HostFactory _factory;

    public Ts089_LabsSemesterFilterNumberDescTests(B03HostFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await B03DomainSeed.AddStandard23LabsAsync(_factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetLabsSemester2NumberDesc_ReturnsThreeLabsDescending()
    {
        // given: анонимный клиент (аутентификации нет).
        var client = B03LabsApi.CreateClient(_factory);

        // when: фильтр по семестру 2, сортировка по номеру по убыванию.
        using var response = await B03LabsApi.GetLabsAsync(
            client, "semester=2&sortField=number&sortDir=desc");

        // then: 200; total=3; номера по убыванию 3,2,1.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.OK, "GET /api/v1/labs?semester=2&sortField=number&sortDir=desc (анонимно)");
        Assert.Equal(3, B03LabsApi.ReadInt(body.RootElement, "total"));

        var pairs = B03LabsApi.ReadItemPairs(body.RootElement);
        Assert.Equal(
            new[] { (2, 3), (2, 2), (2, 1) },
            pairs);
    }
}
