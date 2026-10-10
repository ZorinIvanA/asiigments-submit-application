using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-167 «students: нормализация page и страница правее последней»
/// (boundary, FR-020).
///
/// given: сид демо-данных развёрнут (демо-набор фикстуры): 32 студента
///        (pageSize=10, полных страниц 4, на последней 2 записи); сессия
///        teacher.
/// when:  GET /api/v1/students?page=0; ?page=abc; ?page=2.5; ?page=99.
/// then:  первые три — 200 с page=1 в ответе (эхо некорректного значения
///        запрещено; нормализация как в FR-017) и непустой первой страницей;
///        page=99 — 200 {items:[], total:32, page:99, pageSize:10} — страница
///        правее последней: пустые items при корректном total.
/// </summary>
public sealed class Ts167_StudentsPageNormalizationTests : IClassFixture<B05WebAppFactory>
{
    private const string StudentsEndpoint = "/api/v1/students";
    private const int PageSize = 10;

    private readonly B05WebAppFactory _factory;

    public Ts167_StudentsPageNormalizationTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task NonNormalizedPages_AreNormalizedToOne_WithNonEmptyFirstPage()
    {
        // given: 32 студента сида; teacher авторизован.
        var students = _factory.Services.GetRequiredService<IUserRepository>().ListStudents();
        Assert.Equal(32, students.Count);

        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: page=0; page=abc; page=2.5 (нецелое).
        using var zero = await client.GetAsync($"{StudentsEndpoint}?page=0");
        using var nonNumeric = await client.GetAsync($"{StudentsEndpoint}?page=abc");
        using var fractional = await client.GetAsync($"{StudentsEndpoint}?page=2.5");

        // then: все три — 200 с page=1 (эхо исходного значения запрещено)
        //       и полной первой страницей.
        foreach (var response in new[] { zero, nonNumeric, fractional })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var root = await BodyAssertions.ReadRootObjectAsync(response);
            BodyAssertions.HasExactlyProperties(root, "items", "total", "page", "pageSize");
            Assert.Equal(1, root.GetProperty("page").GetInt32());
            Assert.Equal(32, root.GetProperty("total").GetInt32());
            Assert.Equal(PageSize, root.GetProperty("pageSize").GetInt32());
            Assert.Equal(PageSize, root.GetProperty("items").GetArrayLength());
        }
    }

    [Fact]
    public async Task PageBeyondLast_ReturnsEmptyItemsWithCorrectTotalAndEchoedPage()
    {
        // given: 32 студента сида (полных страниц 4); teacher авторизован.
        var students = _factory.Services.GetRequiredService<IUserRepository>().ListStudents();
        Assert.Equal(32, students.Count);

        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: страница 99 — правее последней (5-й при pageSize=10).
        using var response = await client.GetAsync($"{StudentsEndpoint}?page=99");

        // then: 200 {items:[], total:32, page:99, pageSize:10} — пустые items
        //       при корректном total; номер страницы отдан как есть.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "items", "total", "page", "pageSize");
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
        Assert.Equal(32, root.GetProperty("total").GetInt32());
        Assert.Equal(99, root.GetProperty("page").GetInt32());
        Assert.Equal(PageSize, root.GetProperty("pageSize").GetInt32());
    }
}
