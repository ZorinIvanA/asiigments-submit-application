using LabsApp.IntegrationTests.B03.Infrastructure;
using LabsApp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-108 «Лабораторные: sortField/sortDir вне словаря нормализуются к дефолтам»
/// (boundary, FR-017 «значения вне словаря нормализуются без ошибки»): given —
/// демо-сид (23 работы); клиент анонимный; when — GET /labs?sortField=foo&amp;sortDir=bar;
/// then — 200 total=23; порядок эквивалентен дефолтному (semester↑, number↑);
/// без ошибки (не 400 и не исключение).
/// </summary>
public sealed class Ts108_LabsSortNormalizationTests : IClassFixture<B03HostFactory>,
    IAsyncLifetime
{
    private readonly B03HostFactory _factory;

    public Ts108_LabsSortNormalizationTests(B03HostFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await B03DomainSeed.AddStandard23LabsAsync(_factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetLabsWithOutOfDictionarySortParams_MatchesDefaultOrderWithoutError()
    {
        // given: сид с 23 работами (20 семестра 1 + 3 семестра 2); клиент анонимный.
        Assert.Equal(23, (await _factory.Services.GetRequiredService<ILabRepository>().GetAllAsync()).Count);
        var client = B03LabsApi.CreateClient(_factory);

        // when: sortField=foo, sortDir=bar — вне словаря; и дефолтный список.
        using var garbageSort = await B03LabsApi.GetLabsAsync(client, "sortField=foo&sortDir=bar");
        using var defaultList = await B03LabsApi.GetLabsAsync(client);

        // then: оба — 200 без ошибки (не 400 и не исключение).
        using var garbageBody = await ResponseAssert.ParseWithStatusAsync(
            garbageSort, HttpStatusCode.OK, "GET /api/v1/labs?sortField=foo&sortDir=bar (анонимно)");
        using var defaultBody = await ResponseAssert.ParseWithStatusAsync(
            defaultList, HttpStatusCode.OK, "GET /api/v1/labs (анонимно)");

        // then: total=23, page=1; порядок эквивалентен дефолтному (semester↑, number↑).
        Assert.Equal(23, B03LabsApi.ReadInt(garbageBody.RootElement, "total"));
        Assert.Equal(1, B03LabsApi.ReadInt(garbageBody.RootElement, "page"));
        var garbagePairs = B03LabsApi.ReadItemPairs(garbageBody.RootElement);
        var defaultPairs = B03LabsApi.ReadItemPairs(defaultBody.RootElement);
        Assert.Equal(defaultPairs, garbagePairs);
        Assert.Equal((1, 1), garbagePairs[0]);
    }
}
