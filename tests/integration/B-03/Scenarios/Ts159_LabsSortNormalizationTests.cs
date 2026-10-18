using LabsApp.IntegrationTests.B03.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-159 «labs: некорректные sortField/sortDir нормализуются к дефолтам без
/// ошибки» (boundary, FR-017): given — сид развёрнут: 20 работ семестра 1 и
/// 3 работы семестра 2 (DI-сид <see cref="B03DomainSeed"/>); сессия teacher;
/// when — GET /api/v1/labs?sortField=foo&amp;sortDir=bar; then — 200 без ошибок;
/// выдача идентична дефолтной сортировке semester↑, number↑: total=23, page=1,
/// первый элемент — semester=1, number=1 (значения sortField/sortDir вне словаря
/// нормализуются к дефолтам без ошибки — не 400 и не исключение).
/// </summary>
public sealed class Ts159_LabsSortNormalizationTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts159_LabsSortNormalizationTests(B03HostFactory factory)
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

        // then: оба — 200 без ошибок (не 400 и не исключение).
        using var garbageBody = await ResponseAssert.ParseWithStatusAsync(
            garbageSort, HttpStatusCode.OK, "GET /api/v1/labs?sortField=foo&sortDir=bar (teacher)");
        using var defaultBody = await ResponseAssert.ParseWithStatusAsync(
            defaultList, HttpStatusCode.OK, "GET /api/v1/labs (teacher)");

        // then: total=23, page=1 — выдача идентична дефолтной semester↑,number↑.
        Assert.Equal(23, B03LabsApi.ReadInt(garbageBody.RootElement, "total"));
        Assert.Equal(1, B03LabsApi.ReadInt(garbageBody.RootElement, "page"));
        var garbagePairs = B03LabsApi.ReadItemPairs(garbageBody.RootElement);
        var defaultPairs = B03LabsApi.ReadItemPairs(defaultBody.RootElement);
        Assert.Equal(defaultPairs, garbagePairs);

        // then: первый элемент — semester=1, number=1.
        Assert.Equal(1, garbagePairs[0].Semester);
        Assert.Equal(1, garbagePairs[0].Number);
    }
}
