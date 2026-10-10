using LabsApp.IntegrationTests.B03.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-108 «Лабораторные: sortField/sortDir вне словаря нормализуются к дефолтам»
/// (boundary, FR-017 «значения вне словаря нормализуются без ошибки»): given —
/// демо-сид (23 работы); сессия teacher; when — GET /labs?sortField=foo&amp;sortDir=bar;
/// then — 200 total=23; порядок эквивалентен дефолтному (semester↑, number↑);
/// без ошибки (не 400 и не исключение).
/// </summary>
public sealed class Ts108_LabsSortNormalizationTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts108_LabsSortNormalizationTests(B03HostFactory factory)
    {
        _factory = factory;
        B03DomainSeed.AddStandard23Labs(_factory);
    }

    [Fact]
    public async Task GetLabsWithOutOfDictionarySortParams_MatchesDefaultOrderWithoutError()
    {
        // given: сид с 23 работами (20 семестра 1 + 3 семестра 2); сессия teacher.
        Assert.Equal(23, _factory.Services.GetRequiredService<ILabRepository>().GetAll().Count);
        var (client, _) = B03TeacherSession.Create(_factory);

        // when: sortField=foo, sortDir=bar — вне словаря; и дефолтный список.
        using var garbageSort = await B03LabsApi.GetLabsAsync(client, "sortField=foo&sortDir=bar");
        using var defaultList = await B03LabsApi.GetLabsAsync(client);

        // then: оба — 200 без ошибки (не 400 и не исключение).
        using var garbageBody = await ResponseAssert.ParseWithStatusAsync(
            garbageSort, HttpStatusCode.OK, "GET /api/v1/labs?sortField=foo&sortDir=bar (teacher)");
        using var defaultBody = await ResponseAssert.ParseWithStatusAsync(
            defaultList, HttpStatusCode.OK, "GET /api/v1/labs (teacher)");

        // then: total=23, page=1; порядок эквивалентен дефолтному (semester↑, number↑).
        Assert.Equal(23, B03LabsApi.ReadInt(garbageBody.RootElement, "total"));
        Assert.Equal(1, B03LabsApi.ReadInt(garbageBody.RootElement, "page"));
        var garbagePairs = B03LabsApi.ReadItemPairs(garbageBody.RootElement);
        var defaultPairs = B03LabsApi.ReadItemPairs(defaultBody.RootElement);
        Assert.Equal(defaultPairs, garbagePairs);
        Assert.Equal((1, 1), garbagePairs[0]);
    }
}
