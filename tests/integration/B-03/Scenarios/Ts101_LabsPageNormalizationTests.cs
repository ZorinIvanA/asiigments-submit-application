using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-101 «Лабораторные: нормализация page и страница правее последней» (FR-017 AC
/// «Некорректная страница нормализуется» + «Страница правее последней»; P1): given —
/// в выборке 23 записи при pageSize=10; сессия teacher; when — GET /labs?page=0;
/// ?page=abc; ?page=2.5; ?page=99; then — первые три page=1 в ответе (эхо исходного
/// значения запрещено); четвёртый — items=[], total=23, page=99.
/// </summary>
public sealed class Ts101_LabsPageNormalizationTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts101_LabsPageNormalizationTests(B03HostFactory factory)
    {
        _factory = factory;
        B03DomainSeed.AddStandard23Labs(_factory);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=abc")]
    [InlineData("page=2.5")]
    public async Task GetLabsWithInvalidPage_NormalizesToPage1(string pageQuery)
    {
        // given: сессия teacher; в выборке 23 записи при pageSize=10.
        var (client, _) = B03TeacherSession.Create(_factory);

        // when: GET /labs с некорректным значением page.
        using var response = await B03LabsApi.GetLabsAsync(client, pageQuery);

        // then: 200 и page=1 (нормализованное значение, не эхо входа).
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.OK, $"GET /api/v1/labs?{pageQuery} (teacher)");
        Assert.Equal(1, B03LabsApi.ReadInt(body.RootElement, "page"));
        Assert.Equal(23, B03LabsApi.ReadInt(body.RootElement, "total"));
    }

    [Fact]
    public async Task GetLabsPage99_ReturnsEmptyItemsWithFullTotal()
    {
        // given: сессия teacher; в выборке 23 записи при pageSize=10 (последняя — 3-я).
        var (client, _) = B03TeacherSession.Create(_factory);

        // when: страница 99.
        using var response = await B03LabsApi.GetLabsAsync(client, "page=99");

        // then: 200; items=[]; total=23; page=99.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.OK, "GET /api/v1/labs?page=99 (teacher)");
        Assert.Empty(B03LabsApi.ReadItemPairs(body.RootElement));
        Assert.Equal(23, B03LabsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(99, B03LabsApi.ReadInt(body.RootElement, "page"));
    }
}
