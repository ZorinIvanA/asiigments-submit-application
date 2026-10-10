using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-159 «labs: некорректные sortField/sortDir нормализуются к дефолтам без
/// ошибки» (boundary, FR-017).
///
/// given: сид развёрнут (демо-набор фикстуры): 20 работ семестра 1 и 3 работы
///        семестра 2; сессия teacher.
/// when:  GET /api/v1/labs?sortField=foo&amp;sortDir=bar
/// then:  200 без ошибок; выдача идентична дефолтной сортировке semester↑,
///        number↑: total=23, page=1, первый элемент — semester=1, number=1
///        (значения sortField/sortDir вне словаря нормализуются к дефолтам без
///        ошибки — не 400 и не исключение).
/// </summary>
public sealed class Ts159_LabsSortNormalizationTests : IClassFixture<B05WebAppFactory>
{
    private const string LabsEndpoint = "/api/v1/labs";

    private readonly B05WebAppFactory _factory;

    public Ts159_LabsSortNormalizationTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task OutOfDictionarySortParams_NormalizeToDefaultOrderWithoutError()
    {
        // given: teacher; сид с 23 работами (20 семестра 1 + 3 семестра 2).
        var labs = _factory.Services.GetRequiredService<ILabRepository>().GetAll();
        Assert.Equal(23, labs.Count);

        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: sortField=foo, sortDir=bar — вне словаря; и дефолтный список.
        using var garbageSort = await client.GetAsync($"{LabsEndpoint}?sortField=foo&sortDir=bar");
        using var defaultList = await client.GetAsync(LabsEndpoint);

        // then: 200 без ошибок; параметры нормализованы к semester↑,number↑.
        Assert.Equal(HttpStatusCode.OK, garbageSort.StatusCode);
        Assert.Equal(HttpStatusCode.OK, defaultList.StatusCode);

        var garbageRoot = await BodyAssertions.ReadRootObjectAsync(garbageSort);
        var defaultRoot = await BodyAssertions.ReadRootObjectAsync(defaultList);

        Assert.Equal(23, garbageRoot.GetProperty("total").GetInt32());
        Assert.Equal(1, garbageRoot.GetProperty("page").GetInt32());

        var garbageItems = garbageRoot.GetProperty("items").Clone();
        var defaultItems = defaultRoot.GetProperty("items").Clone();
        Assert.True(
            JsonElement.DeepEquals(garbageItems, defaultItems),
            "Выдача с sortField=foo/sortDir=bar не идентична дефолтной сортировке semester↑,number↑.");

        var first = garbageItems.EnumerateArray().First();
        Assert.Equal(1, first.GetProperty("semester").GetInt32());
        Assert.Equal(1, first.GetProperty("number").GetInt32());
    }
}
