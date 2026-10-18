using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-174 «labs: sortDir=desc с межсеместровым вторичным ключом semester asc
/// (ревью R4e)» (boundary, FR-017): given — сид развёрнут: 20 работ семестра 1
/// (№1–20) и 3 работы семестра 2 (№1–3) — номера 1, 2, 3 существуют в ОБОИХ
/// семестрах (межсеместровые дубли номеров, DI-сид <see cref="B03DomainSeed"/>);
/// фильтр semester НЕ применяется; сессия teacher; when — GET
/// /api/v1/labs?sortField=number&amp;sortDir=desc; then — 200, total=23,
/// pageSize=10; порядок: первичный ключ number по убыванию, вторичный semester
/// ВСЕГДА по возрастанию — первый элемент (semester=1, number=20); сквозной
/// порядок по всей выборке (3 страницы) завершается последовательностью …,
/// (1,4), (1,3), (2,3), (1,2), (2,2), (1,1), (2,1) — при вторичном desc хвост
/// отличался бы: (2,3), (1,3), (2,2), (1,2), (2,1), (1,1).
/// </summary>
public sealed class Ts174_LabsSortDescSecondarySemesterTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts174_LabsSortDescSecondarySemesterTests(B03HostFactory factory)
    {
        _factory = factory;
        B03DomainSeed.AddStandard23Labs(_factory);
    }

    [Fact]
    public async Task NumberDesc_SecondarySemesterAlwaysAsc_AcrossAllPages()
    {
        // given: сид 23 работ с межсеместровыми дублями номеров 1..3; teacher.
        var (client, _) = B03TeacherSession.Create(_factory);

        // when: сквозная выборка sortField=number&sortDir=desc (фильтр semester
        //       не передан) — три страницы по pageSize=10 (на последней 3 записи).
        var actualPairs = new List<(int Semester, int Number)>(23);
        for (var page = 1; page <= 3; page++)
        {
            using var response = await B03LabsApi.GetLabsAsync(
                client, $"sortField=number&sortDir=desc&page={page}");
            using var body = await ResponseAssert.ParseWithStatusAsync(
                response,
                HttpStatusCode.OK,
                $"GET /api/v1/labs?sortField=number&sortDir=desc&page={page} (teacher)");

            // then: на каждой странице total=23 (фильтр semester не применён),
            //       pageSize=10, page — нормализованное значение.
            Assert.Equal(23, B03LabsApi.ReadInt(body.RootElement, "total"));
            Assert.Equal(10, B03LabsApi.ReadInt(body.RootElement, "pageSize"));
            Assert.Equal(page, B03LabsApi.ReadInt(body.RootElement, "page"));

            actualPairs.AddRange(B03LabsApi.ReadItemPairs(body.RootElement));
        }

        // then: первый элемент — (semester=1, number=20) (number desc).
        Assert.Equal(23, actualPairs.Count);
        Assert.Equal((Semester: 1, Number: 20), actualPairs[0]);

        // then: сквозной порядок — первичный number по убыванию, вторичный
        //       semester ВСЕГДА по возрастанию: номера 20..4 существуют только
        //       в семестре 1, номера 3/2/1 — в обоих семестрах (семестр asc).
        var expectedPairs = new List<(int Semester, int Number)>(23);
        for (var number = 20; number >= 4; number--)
        {
            expectedPairs.Add((1, number));
        }

        foreach (var number in new[] { 3, 2, 1 })
        {
            expectedPairs.Add((1, number));
            expectedPairs.Add((2, number));
        }

        Assert.Equal(expectedPairs, actualPairs);
    }
}
