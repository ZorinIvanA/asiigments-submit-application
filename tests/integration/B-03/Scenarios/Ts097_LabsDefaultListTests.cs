using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-097 «Лабораторные: дефолтный список — пагинация и порядок» (FR-017 AC
/// «Дефолтный список»; P0): given — сид развёрнут: 20 работ семестра 1 и 3 работы
/// семестра 2 (DI-сид при Seed__DemoData=false); сессия teacher; when — GET
/// /api/v1/labs; then — 200 {items:10 записей, total:23, page:1, pageSize:10};
/// первый элемент — semester=1, number=1 (дефолтная сортировка semester↑, number↑).
/// </summary>
public sealed class Ts097_LabsDefaultListTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts097_LabsDefaultListTests(B03HostFactory factory)
    {
        _factory = factory;
        B03DomainSeed.AddStandard23Labs(_factory);
    }

    [Fact]
    public async Task GetLabsWithoutParameters_ReturnsFirstPageOfDefaultSorted23()
    {
        // given: сессия teacher.
        var (client, _) = B03TeacherSession.Create(_factory);

        // when: GET /api/v1/labs без параметров.
        using var response = await B03LabsApi.GetLabsAsync(client);

        // then: 200; {items:10, total:23, page:1, pageSize:10}; первый элемент (1,1).
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.OK, "GET /api/v1/labs без параметров (teacher)");
        Assert.Equal(10, B03LabsApi.ReadInt(body.RootElement, "pageSize"));
        Assert.Equal(23, B03LabsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(1, B03LabsApi.ReadInt(body.RootElement, "page"));

        var pairs = B03LabsApi.ReadItemPairs(body.RootElement);
        Assert.Equal(10, pairs.Length);
        Assert.Equal((1, 1), pairs[0]);
    }
}
