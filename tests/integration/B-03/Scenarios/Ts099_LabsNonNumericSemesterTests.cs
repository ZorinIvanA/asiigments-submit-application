using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-099 «Лабораторные: нечисловой semester — фильтр не применяется (ISS-009)»
/// (FR-017 AC «Нечисловой semester — фильтр не применяется (ISS-009)»; P0): given —
/// сид развёрнут (23 работы); сессия teacher; when — GET /labs?semester=abc; затем
/// GET /labs?semester=2.5; then — оба 200 с total=23 и page=1: поведение совпадает
/// с запросом без параметра semester (мягкая нормализация без ошибки).
/// </summary>
public sealed class Ts099_LabsNonNumericSemesterTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts099_LabsNonNumericSemesterTests(B03HostFactory factory)
    {
        _factory = factory;
        B03DomainSeed.AddStandard23Labs(_factory);
    }

    [Fact]
    public async Task GetLabsWithNonNumericSemester_AppliesNoFilter()
    {
        // given: сессия teacher.
        var (client, _) = B03TeacherSession.Create(_factory);

        // when/then: ?semester=abc — нечисловая строка: 200, total=23, page=1.
        using var alpha = await B03LabsApi.GetLabsAsync(client, "semester=abc");
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            alpha, HttpStatusCode.OK, "GET /api/v1/labs?semester=abc (teacher)"))
        {
            Assert.Equal(23, B03LabsApi.ReadInt(body.RootElement, "total"));
            Assert.Equal(1, B03LabsApi.ReadInt(body.RootElement, "page"));
        }

        // when/then: ?semester=2.5 — дробное значение: тот же контракт мягкой нормализации.
        using var fractional = await B03LabsApi.GetLabsAsync(client, "semester=2.5");
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            fractional, HttpStatusCode.OK, "GET /api/v1/labs?semester=2.5 (teacher)"))
        {
            Assert.Equal(23, B03LabsApi.ReadInt(body.RootElement, "total"));
            Assert.Equal(1, B03LabsApi.ReadInt(body.RootElement, "page"));
        }
    }
}
