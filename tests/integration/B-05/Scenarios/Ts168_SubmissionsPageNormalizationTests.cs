using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-168 «submissions: нормализация page и страница правее последней»
/// (boundary, FR-021).
///
/// given: ИК-221 (25 студентов, страница по 5 — полных страниц 5), семестр 1
///        (20 работ); сессия teacher; Labs__MaxSemester=10 (умолчание
///        конфигурации).
/// when:  GET /api/v1/submissions?groupId=&lt;ИК-221&gt;&amp;semester=1&amp;page=0;
///        ?page=abc; ?page=99.
/// then:  page=0 и page=abc — 200 с page=1 и первой страницей студентов
///        (5 записей) — эхо некорректного значения запрещено; page=99 —
///        200 {students:[], labs:20 (number↑), submissions:[], total:25,
///        page:99} — нормализованный int и пустая страница студентов при
///        корректном total, submissions только для пар текущей (пустой)
///        страницы (нормализация как в FR-017).
/// </summary>
public sealed class Ts168_SubmissionsPageNormalizationTests : IClassFixture<B05WebAppFactory>
{
    private const string GridEndpoint = "/api/v1/submissions";
    private const int GridPageSize = 5;

    private readonly B05WebAppFactory _factory;

    public Ts168_SubmissionsPageNormalizationTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Zero_And_NonNumeric_Page_AreNormalizedToOne_WithFirstStudentPage()
    {
        // given: ИК-221 с 25 студентами; 20 работ семестра 1; teacher.
        var ik221 = B05SeedLookup.GroupByName(_factory, "ИК-221");
        Assert.Equal(25, _factory.Services.GetRequiredService<IUserRepository>().ListByGroup(ik221.Id).Count);
        Assert.Equal(20, _factory.Services.GetRequiredService<ILabRepository>().ListByFilter(1).Count);

        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: page=0 и page=abc (некорректные значения страницы).
        using var zero = await client.GetAsync(
            $"{GridEndpoint}?groupId={ik221.Id}&semester=1&page=0");
        using var nonNumeric = await client.GetAsync(
            $"{GridEndpoint}?groupId={ik221.Id}&semester=1&page=abc");

        // then: оба — 200 с page=1 и первой страницей студентов (5 записей);
        //       эхо некорректного значения запрещено.
        foreach (var response in new[] { zero, nonNumeric })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var root = await BodyAssertions.ReadRootObjectAsync(response);
            BodyAssertions.HasExactlyProperties(
                root, "students", "labs", "submissions", "total", "page");
            Assert.Equal(1, root.GetProperty("page").GetInt32());
            Assert.Equal(25, root.GetProperty("total").GetInt32());
            Assert.Equal(GridPageSize, root.GetProperty("students").GetArrayLength());
        }
    }

    [Fact]
    public async Task PageBeyondLast_ReturnsEmptyStudentsAndSubmissions_WithSemesterLabsAndCorrectTotal()
    {
        // given: ИК-221 с 25 студентами (полных страниц 5 по 5); teacher.
        var ik221 = B05SeedLookup.GroupByName(_factory, "ИК-221");
        Assert.Equal(25, _factory.Services.GetRequiredService<IUserRepository>().ListByGroup(ik221.Id).Count);

        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: страница 99 — правее последней (6-й при pageSize=5).
        using var response = await client.GetAsync(
            $"{GridEndpoint}?groupId={ik221.Id}&semester=1&page=99");

        // then: 200 {students:[], labs:20 (number↑), submissions:[], total:25,
        //       page:99} — колонки работ семестра полные, сдач нет (пары
        //       только текущей — пустой — страницы студентов).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(
            root, "students", "labs", "submissions", "total", "page");

        Assert.Equal(0, root.GetProperty("students").GetArrayLength());
        Assert.Equal(0, root.GetProperty("submissions").GetArrayLength());
        Assert.Equal(25, root.GetProperty("total").GetInt32());
        Assert.Equal(99, root.GetProperty("page").GetInt32());

        var labNumbers = root.GetProperty("labs").EnumerateArray()
            .Select(lab => lab.GetProperty("number").GetInt32())
            .ToList();
        Assert.Equal(20, labNumbers.Count);
        Assert.Equal(Enumerable.Range(1, 20).ToList(), labNumbers);
    }
}
