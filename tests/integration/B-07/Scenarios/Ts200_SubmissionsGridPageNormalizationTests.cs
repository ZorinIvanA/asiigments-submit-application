using System.Text.Json;
using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-200 «Ведомость: некорректная page нормализуется, страница правее
/// последней — пустая students-страница» (boundary, FR-021, P1).
///
/// given: ИК-221 (25 студентов), семестр 1 (20 работ, демо-сид FR-025);
///        сессия teacher (минт access-JWT, ADR-015).
/// when:  GET /api/v1/submissions?groupId=&lt;ИК-221.id&gt;&amp;semester=1&amp;page=0;
///        затем ?page=abc; затем ?page=2.5; затем ?page=99.
/// then:  первые три запроса — 200 с page=1 (нормализация по правилу FR-017,
///        применённому к /submissions контрактом «page (некорректное → 1)»,
///        эхо некорректного значения запрещено); ?page=99 — 200: students=[]
///        и submissions=[] при total=25 и page=99, labs — по-прежнему 20 работ
///        семестра (правее последней страницы выдача студентов пуста, total
///        корректен; FR-021: students — страница по 5, submissions — только
///        пары текущей страницы).
/// </summary>
public sealed class Ts200_SubmissionsGridPageNormalizationTests : IClassFixture<B07DemoSeedWebAppFactory>
{
    /// <summary>Размер страницы студентов ведомости (FR-021: по 5).</summary>
    private const int PageSize = 5;

    /// <summary>Число студентов демо-группы ИК-221 (сид FR-025: student01..student25).</summary>
    private const int Ik221StudentCount = 25;

    /// <summary>Число работ семестра 1 демо-сида (сид FR-025: №1–20).</summary>
    private const int Semester1LabCount = 20;

    private readonly B07DemoSeedWebAppFactory _factory;

    public Ts200_SubmissionsGridPageNormalizationTests(B07DemoSeedWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetGridWithPage0_NormalizesToPage1()
    {
        // given: сессия teacher; демо-группа ИК-221 (25 студентов), семестр 1 (20 работ).
        using var client = B07MintedSessions.CreateTeacherClient(_factory);
        var ik221Id = GetIk221Id();

        // when: GET /submissions?groupId=<ИК-221>&semester=1&page=0.
        using var response = await client.GetAsync($"/api/v1/submissions?groupId={ik221Id}&semester=1&page=0");

        // then: 200 с page=1 (нормализованное значение, эхо «0» запрещено);
        // students — первая страница по 5, labs — 20 работ семестра.
        var body = await ReadGridBodyAsync(response, "page=0");
        Assert.Equal(1, ReadInt(body, "page"));
        Assert.Equal(Ik221StudentCount, ReadInt(body, "total"));
        Assert.Equal(PageSize, ArrayLength(body, "students"));
        Assert.Equal(Semester1LabCount, ArrayLength(body, "labs"));
    }

    [Fact]
    public async Task GetGridWithNonNumericPage_NormalizesToPage1()
    {
        // given: сессия teacher; демо-группа ИК-221 (25 студентов), семестр 1 (20 работ).
        using var client = B07MintedSessions.CreateTeacherClient(_factory);
        var ik221Id = GetIk221Id();

        // when: GET /submissions?groupId=<ИК-221>&semester=1&page=abc.
        using var response = await client.GetAsync($"/api/v1/submissions?groupId={ik221Id}&semester=1&page=abc");

        // then: 200 с page=1 (нормализованное значение, эхо «abc» запрещено).
        var body = await ReadGridBodyAsync(response, "page=abc");
        Assert.Equal(1, ReadInt(body, "page"));
        Assert.Equal(Ik221StudentCount, ReadInt(body, "total"));
    }

    [Fact]
    public async Task GetGridWithFractionalPage_NormalizesToPage1()
    {
        // given: сессия teacher; демо-группа ИК-221 (25 студентов), семестр 1 (20 работ).
        using var client = B07MintedSessions.CreateTeacherClient(_factory);
        var ik221Id = GetIk221Id();

        // when: GET /submissions?groupId=<ИК-221>&semester=1&page=2.5.
        using var response = await client.GetAsync($"/api/v1/submissions?groupId={ik221Id}&semester=1&page=2.5");

        // then: 200 с page=1 (нецелое нормализуется, эхо «2.5» запрещено).
        var body = await ReadGridBodyAsync(response, "page=2.5");
        Assert.Equal(1, ReadInt(body, "page"));
        Assert.Equal(Ik221StudentCount, ReadInt(body, "total"));
    }

    [Fact]
    public async Task GetGridWithPageBeyondLast_ReturnsEmptyStudentsWithCorrectTotal()
    {
        // given: сессия teacher; демо-группа ИК-221 (25 студентов), семестр 1 (20 работ).
        using var client = B07MintedSessions.CreateTeacherClient(_factory);
        var ik221Id = GetIk221Id();

        // when: GET /submissions?groupId=<ИК-221>&semester=1&page=99.
        using var response = await client.GetAsync($"/api/v1/submissions?groupId={ik221Id}&semester=1&page=99");

        // then: 200: students=[] и submissions=[] (пар текущей страницы нет)
        // при total=25 и page=99; labs — по-прежнему 20 работ семестра.
        var body = await ReadGridBodyAsync(response, "page=99");
        Assert.Equal(0, ArrayLength(body, "students"));
        Assert.Equal(0, ArrayLength(body, "submissions"));
        Assert.Equal(Ik221StudentCount, ReadInt(body, "total"));
        Assert.Equal(99, ReadInt(body, "page"));
        Assert.Equal(Semester1LabCount, ArrayLength(body, "labs"));
    }

    // ------------------------------------------------------------------
    // Шаги given/then: демо-группа и разбор тела ведомости
    // ------------------------------------------------------------------

    /// <summary>id демо-группы ИК-221 (DI-чтение IGroupRepository, IF-015):
    /// шаг given «ИК-221 (25 студентов)» — сида FR-025, Seed__DemoData=true.</summary>
    private Guid GetIk221Id() =>
        _factory.Services.GetRequiredService<IGroupRepository>().GetByName("ИК-221") is { } ik221
            ? ik221.Id
            : throw new InvalidOperationException(
                "Демо-группа «ИК-221» не найдена — шаг given «демо-сид» неисполним (Seed__DemoData=true).");

    /// <summary>Разбирает тело ответа ведомости: 200 и JSON-объект — иначе
    /// падение с фактическим статусом/телом (диагностика, механика зоны).</summary>
    private static async Task<JsonElement> ReadGridBodyAsync(HttpResponseMessage response, string pageLabel)
    {
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"GET /submissions (page {pageLabel}, teacher): ожидался 200, фактически {(int)response.StatusCode}.");
        return await BodyAssertions.ReadRootObjectAsync(response);
    }

    /// <summary>Целочисленное свойство тела ведомости (total/page).</summary>
    private static int ReadInt(JsonElement root, string field)
    {
        Assert.True(
            root.TryGetProperty(field, out var property),
            $"В теле ведомости отсутствует ключ «{field}»: {root.GetRawText()}");
        Assert.Equal(JsonValueKind.Number, property.ValueKind);
        return property.GetInt32();
    }

    /// <summary>Длина массива тела ведомости (students/labs/submissions).</summary>
    private static int ArrayLength(JsonElement root, string field)
    {
        Assert.True(
            root.TryGetProperty(field, out var property),
            $"В теле ведомости отсутствует ключ «{field}»: {root.GetRawText()}");
        Assert.Equal(JsonValueKind.Array, property.ValueKind);
        return property.GetArrayLength();
    }
}
