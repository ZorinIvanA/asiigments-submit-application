using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-110 «Лабораторные: внедиапазонный semester — пустая выборка, не 400»
/// (boundary, FR-017 AC «Внедиапазонный semester»; P0): given — работы только
/// в семестрах 1 и 2 (демо-сид); Labs__MaxSemester=10; сессия teacher; when —
/// GET /labs?semester=99; then — 200 {items:[], total:0, page:1, pageSize:10}
/// (НЕ 400).
/// </summary>
public sealed class Ts110_LabsOutOfRangeSemesterTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts110_LabsOutOfRangeSemesterTests(B03HostFactory factory)
    {
        _factory = factory;
        B03DomainSeed.AddStandard23Labs(_factory);
    }

    [Fact]
    public async Task GetLabsSemester99_ReturnsEmptyPageNot400()
    {
        // given: сессия teacher; сид — работы только в семестрах 1 и 2.
        var (client, _) = B03TeacherSession.Create(_factory);

        // when: целочисленный semester вне диапазона 1..Labs__MaxSemester.
        using var response = await B03LabsApi.GetLabsAsync(client, "semester=99");

        // then: 200 {items:[], total:0, page:1, pageSize:10} (НЕ 400).
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.OK, "GET /api/v1/labs?semester=99 (teacher)");
        Assert.Empty(B03LabsApi.ReadItemPairs(body.RootElement));
        Assert.Equal(0, B03LabsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(1, B03LabsApi.ReadInt(body.RootElement, "page"));
        Assert.Equal(10, B03LabsApi.ReadInt(body.RootElement, "pageSize"));
    }
}
