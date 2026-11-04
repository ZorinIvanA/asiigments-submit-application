using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-107 «Лабораторные: вторичный ключ сортировки всегда asc» (boundary, FR-017):
/// given — созданы две работы с одинаковым semester=3 (number 2 и 1) — DI-сид
/// (<see cref="B03DomainSeed"/>); клиент анонимный; when — GET
/// /labs?semester=3&amp;sortField=semester&amp;sortDir=desc; then — 200; внутри
/// семестра порядок number 1,2 (вторичный ключ — соседнее поле всегда asc,
/// независимо от направления первичного).
/// </summary>
public sealed class Ts107_LabsSecondarySortAscTests : IClassFixture<B03HostFactory>,
    IAsyncLifetime
{
    private readonly B03HostFactory _factory;

    public Ts107_LabsSecondarySortAscTests(B03HostFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await B03DomainSeed.AddLabsAsync(_factory, (3, 2), (3, 1));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetLabsSemesterDescSecondaryNumberStaysAsc()
    {
        // given: клиент анонимный; в семестре 3 ровно две работы (number 2 и 1).
        var client = B03LabsApi.CreateClient(_factory);

        // when: первичный ключ semester desc (единственный семестр выборки) —
        // направление первичного ключа не должно влиять на вторичный.
        using var response = await B03LabsApi.GetLabsAsync(
            client, "semester=3&sortField=semester&sortDir=desc");

        // then: 200; total=2; внутри семестра порядок number 1,2 (вторичный asc).
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.OK, "GET /api/v1/labs?semester=3&sortField=semester&sortDir=desc (анонимно)");
        Assert.Equal(2, B03LabsApi.ReadInt(body.RootElement, "total"));

        var pairs = B03LabsApi.ReadItemPairs(body.RootElement);
        Assert.Equal(
            new[] { (3, 1), (3, 2) },
            pairs);
    }
}
