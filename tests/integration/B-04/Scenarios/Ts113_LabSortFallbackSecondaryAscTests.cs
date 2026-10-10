using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-113 (P2, boundary; FR-017) «Лабораторные: нормализация sortField/sortDir
/// и вторичный ключ asc».
/// given: созданы работы (1,1),(1,2),(2,1),(2,2); teacher.
/// when:  GET /labs?sortField=garbage&amp;sortDir=garbage; затем
///        GET /labs?sortField=number&amp;sortDir=desc.
/// then:  первый запрос — порядок (1,1),(1,2),(2,1),(2,2) (значения вне словаря
///        нормализованы к дефолтам semester↑,number↑); второй — числа 2,2,1,1,
///        внутри одного number — semester по возрастанию (вторичный ключ всегда
///        asc).
/// </summary>
public sealed class Ts113_LabSortFallbackSecondaryAscTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS113_GetLabs_SortFallbackThenNumberDescWithAscTiebreak()
    {
        // given: работы (1,1),(1,2),(2,1),(2,2); teacher.
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        B04DomainSeed.AddLab(labs, semester: 1, number: 1);
        B04DomainSeed.AddLab(labs, semester: 1, number: 2);
        B04DomainSeed.AddLab(labs, semester: 2, number: 1);
        B04DomainSeed.AddLab(labs, semester: 2, number: 2);
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: sortField/sortDir вне словаря.
        using var fallback = await client.GetAsync("/api/v1/labs?sortField=garbage&sortDir=garbage");

        // then: 200; порядок — дефолтный semester↑, number↑.
        var fallbackRoot = await ApiAssert.ReadOkJsonAsync(fallback);
        (int Semester, int Number)[] defaultOrder = [(1, 1), (1, 2), (2, 1), (2, 2)];
        Assert.Equal(defaultOrder, PairsOf(fallbackRoot));

        // when: затем первичный ключ number по убыванию.
        using var byNumber = await client.GetAsync("/api/v1/labs?sortField=number&sortDir=desc");

        // then: числа 2,2,1,1; внутри одного number — semester по возрастанию
        // (вторичный ключ всегда asc): (1,2),(2,2),(1,1),(2,1).
        var byNumberRoot = await ApiAssert.ReadOkJsonAsync(byNumber);
        (int Semester, int Number)[] numberDescOrder = [(1, 2), (2, 2), (1, 1), (2, 1)];
        Assert.Equal(numberDescOrder, PairsOf(byNumberRoot));
    }

    /// <summary>Пары (semester, number) элементов страницы в порядке выдачи.</summary>
    private static (int Semester, int Number)[] PairsOf(JsonElement root) =>
        root.GetProperty("items")
            .EnumerateArray()
            .Select(item => (item.GetProperty("semester").GetInt32(), item.GetProperty("number").GetInt32()))
            .ToArray();
}
