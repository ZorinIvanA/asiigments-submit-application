using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-133 «Студенты: поиск ровно 200 символов — валиден» (boundary, FR-020).
///
/// given: teacher (минт сессии, ADR-015).
/// when:  GET /api/v1/students?search=&lt;строка из 200 символов&gt;
/// then:  200; PagedResult с total=0 (ничего не найдено), page=1, pageSize=10 —
///        НЕ 400: граница 200 включительно (FR-020).
/// </summary>
public sealed class Ts133_StudentsSearchBoundary200Tests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts133_StudentsSearchBoundary200Tests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SearchOfExactly200Chars_IsAcceptedAndReturnsEmptyPage()
    {
        // given: teacher авторизован.
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: search ровно 200 символов (ни один студент не содержит такую подстроку).
        var search = Uri.EscapeDataString(new string('x', 200));
        using var response = await client.GetAsync($"/api/v1/students?search={search}");

        // then: 200 (не 400); пустая страница с нормализованной пагинацией.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(10, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
    }
}
