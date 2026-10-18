using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-109 «Лабораторные: нечисловой semester — фильтр не применяется (мягкая
/// нормализация)» (boundary, FR-017 AC «Нечисловой semester»; P0): given —
/// демо-сид: 23 работы; сессия teacher; when — GET /labs?semester=abc и GET
/// /labs?semester=2.5; then — оба — 200 с total=23 и page=1 (поведение
/// совпадает с запросом без параметра semester; НЕ 400).
/// </summary>
public sealed class Ts109_LabsNonNumericSemesterTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts109_LabsNonNumericSemesterTests(B03HostFactory factory)
    {
        _factory = factory;
        B03DomainSeed.AddStandard23Labs(_factory);
    }

    [Fact]
    public async Task GetLabsWithNonNumericSemester_AppliesNoFilter()
    {
        // given: сессия teacher.
        var (client, _) = B03TeacherSession.Create(_factory);

        // when: обе попытки мягкой нормализации и эталонный запрос без semester.
        using var alpha = await B03LabsApi.GetLabsAsync(client, "semester=abc");
        using var fractional = await B03LabsApi.GetLabsAsync(client, "semester=2.5");
        using var noFilter = await B03LabsApi.GetLabsAsync(client);

        (int Semester, int Number)[] alphaPairs;
        (int Semester, int Number)[] fractionalPairs;
        (int Semester, int Number)[] noFilterPairs;

        // then: оба — 200 с total=23 и page=1 (НЕ 400).
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            alpha, HttpStatusCode.OK, "GET /api/v1/labs?semester=abc (teacher)"))
        {
            Assert.Equal(23, B03LabsApi.ReadInt(body.RootElement, "total"));
            Assert.Equal(1, B03LabsApi.ReadInt(body.RootElement, "page"));
            alphaPairs = B03LabsApi.ReadItemPairs(body.RootElement);
        }

        using (var body = await ResponseAssert.ParseWithStatusAsync(
            fractional, HttpStatusCode.OK, "GET /api/v1/labs?semester=2.5 (teacher)"))
        {
            Assert.Equal(23, B03LabsApi.ReadInt(body.RootElement, "total"));
            Assert.Equal(1, B03LabsApi.ReadInt(body.RootElement, "page"));
            fractionalPairs = B03LabsApi.ReadItemPairs(body.RootElement);
        }

        // then: поведение совпадает с запросом без параметра semester (выдача та же).
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            noFilter, HttpStatusCode.OK, "GET /api/v1/labs без semester (teacher)"))
        {
            noFilterPairs = B03LabsApi.ReadItemPairs(body.RootElement);
        }

        // Эталонный запрос без semester отдаёт первую страницу (pageSize=10) —
        // сравниваются первые страницы выборок: они обязаны совпадать.
        Assert.Equal(10, noFilterPairs.Length);
        Assert.Equal(noFilterPairs, alphaPairs);
        Assert.Equal(noFilterPairs, fractionalPairs);
    }
}
