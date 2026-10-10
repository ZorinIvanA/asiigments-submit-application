using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-117 (P2, boundary; FR-017 «значения вне словаря нормализуются к дефолтам
/// без ошибки») «Лабораторные: посторонние sortField/sortDir нормализуются к дефолтам».
/// given: сид развёрнут (23 работы).
/// when:  GET /labs?sortField=color&amp;sortDir=sideways.
/// then:  200; порядок совпадает с дефолтным (semester↑, number↑; первый элемент —
///        semester=1, number=1); page=1; без ошибки.
/// </summary>
public sealed class Ts117_LabSortNormalizationTests(B04DemoSeedWebAppFactory factory)
    : IClassFixture<B04DemoSeedWebAppFactory>
{
    private readonly B04DemoSeedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS117_GetLabsWithForeignSortParams_FallsBackToDefaultOrder()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: GET /labs с sortField/sortDir вне словаря.
        using var response = await client.GetAsync("/api/v1/labs?sortField=color&sortDir=sideways");

        // then: 200 без ошибки; page=1; порядок — дефолтный semester↑, number↑:
        // первая страница из 23 сид-работ — (1,1)..(1,10), первый элемент (1,1).
        var root = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(23, root.GetProperty("total").GetInt32());
        Assert.Equal(10, root.GetProperty("pageSize").GetInt32());

        var items = root.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(10, items.GetArrayLength());

        for (var index = 0; index < items.GetArrayLength(); index++)
        {
            var item = items[index];
            Assert.Equal(1, item.GetProperty("semester").GetInt32());
            Assert.Equal(index + 1, item.GetProperty("number").GetInt32());
        }

        Assert.Equal(1, items[0].GetProperty("semester").GetInt32());
        Assert.Equal(1, items[0].GetProperty("number").GetInt32());
    }
}
