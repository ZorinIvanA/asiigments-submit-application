using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-090 «labs: semester=abc/2.5 — фильтр не применяется; semester=99 — пустая
/// выборка» (FR-017 AC «Нечисловой semester», «Внедиапазонный semester», ISS-009/
/// AR-002/SEC-005; P0): given — сид: 23 работы; клиент анонимный; when — GET
/// /labs?semester=abc; GET /labs?semester=2.5; GET /labs?semester=99; then — первые
/// два 200 с total=23 и page=1 (мягкая нормализация, поведение как без параметра);
/// третий — 200 {items:[], total:0, page:1, pageSize:10} — НЕ 400.
/// </summary>
public sealed class Ts090_LabsSemesterFilterBoundaryTests : IClassFixture<B03HostFactory>,
    IAsyncLifetime
{
    private readonly B03HostFactory _factory;

    public Ts090_LabsSemesterFilterBoundaryTests(B03HostFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await B03DomainSeed.AddStandard23LabsAsync(_factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetLabsWithNonNumericSemester_AppliesNoFilter()
    {
        // given: анонимный клиент (аутентификации нет).
        var client = B03LabsApi.CreateClient(_factory);

        // when/then: ?semester=abc — нечисловая строка: 200, total=23, page=1.
        using var alpha = await B03LabsApi.GetLabsAsync(client, "semester=abc");
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            alpha, HttpStatusCode.OK, "GET /api/v1/labs?semester=abc (анонимно)"))
        {
            Assert.Equal(23, B03LabsApi.ReadInt(body.RootElement, "total"));
            Assert.Equal(1, B03LabsApi.ReadInt(body.RootElement, "page"));
        }

        // when/then: ?semester=2.5 — дробное значение: тот же контракт мягкой нормализации.
        using var fractional = await B03LabsApi.GetLabsAsync(client, "semester=2.5");
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            fractional, HttpStatusCode.OK, "GET /api/v1/labs?semester=2.5 (анонимно)"))
        {
            Assert.Equal(23, B03LabsApi.ReadInt(body.RootElement, "total"));
            Assert.Equal(1, B03LabsApi.ReadInt(body.RootElement, "page"));
        }
    }

    [Fact]
    public async Task GetLabsSemester99_ReturnsEmptyPageNot400()
    {
        // given: клиент анонимный; сид — работы только в семестрах 1 и 2.
        var client = B03LabsApi.CreateClient(_factory);

        // when: целочисленный semester вне диапазона.
        using var response = await B03LabsApi.GetLabsAsync(client, "semester=99");

        // then: 200 с пустой выборкой и корректной пагинацией (НЕ 400).
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.OK, "GET /api/v1/labs?semester=99 (анонимно)");
        var pairs = B03LabsApi.ReadItemPairs(body.RootElement);
        Assert.Empty(pairs);
        Assert.Equal(0, B03LabsApi.ReadInt(body.RootElement, "total"));
        Assert.Equal(1, B03LabsApi.ReadInt(body.RootElement, "page"));
        Assert.Equal(10, B03LabsApi.ReadInt(body.RootElement, "pageSize"));
    }
}
