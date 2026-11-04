using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-105 «Лабораторные: дефолтный список — пагинация 10 и дефолтная сортировка»
/// (happy_path, FR-017 AC «Дефолтный список»; P0): given — демо-сид: 20 работ
/// семестра 1 (№1–20) и 3 работы семестра 2 (№1–3) — DI-сид при Seed__DemoData=false
/// (<see cref="B03DomainSeed"/>); клиент анонимный (эндпойнты публичны);
/// when — GET /api/v1/labs; then — 200 {items:10, total:23, page:1, pageSize:10};
/// первый элемент (semester=1, number=1); сортировка по умолчанию semester↑, number↑.
/// </summary>
public sealed class Ts105_LabsDefaultListTests : IClassFixture<B03HostFactory>,
    IAsyncLifetime
{
    private readonly B03HostFactory _factory;

    public Ts105_LabsDefaultListTests(B03HostFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await B03DomainSeed.AddStandard23LabsAsync(_factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetLabsWithoutParameters_ReturnsFirstPageOfDefaultSorted23()
    {
        // given: анонимный клиент (аутентификации нет).
        var client = B03LabsApi.CreateClient(_factory);

        // when: GET /api/v1/labs без параметров.
        using var response = await B03LabsApi.GetLabsAsync(client);

        // then: 200; {items:10, total:23, page:1, pageSize:10}.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.OK, "GET /api/v1/labs без параметров (анонимно)");
        Assert.Equal(10, B03LabsApi.ReadInt(body.RootElement, "pageSize"));
        Assert.Equal(23, B03LabsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(1, B03LabsApi.ReadInt(body.RootElement, "page"));

        // then: первый элемент (semester=1, number=1); сортировка по умолчанию
        // semester↑, number↑ — первая страница (1,1)..(1,10).
        var pairs = B03LabsApi.ReadItemPairs(body.RootElement);
        Assert.Equal(
            Enumerable.Range(1, 10).Select(number => (1, number)).ToArray(),
            pairs);
    }
}
