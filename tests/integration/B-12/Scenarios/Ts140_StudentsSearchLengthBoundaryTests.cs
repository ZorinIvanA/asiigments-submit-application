using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-140 (P0, boundary; FR-050) «GET /students: граница длины search 200/201».
/// given: Сессия teacher.
/// when: search ровно из 200 символов; отдельно из 201.
/// then: 200 → обычный ответ 200; 201 → 400 «Данные заполнены неверно» с
/// errors.search=['Поиск — не более 200 символов']. FR-050 AC «Граница search».
/// </summary>
public sealed class Ts140_StudentsSearchLengthBoundaryTests(B12WebAppFactory factory) : IClassFixture<B12WebAppFactory>
{
    private const int MaxSearchLength = 200;

    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS140_Search_Exactly200Chars_ReturnsNormalPage()
    {
        // given: сессия teacher; search ровно из 200 символов; в хранилище студент,
        // ни одно поле которого не содержит ни 'x', ни 200-символьной последовательности
        // (CR-002: иначе total=0 неотличим от «поиск игнорируется»).
        B12Seed.EnsureStudent(
            _factory,
            login: "b12s140",
            fullName: "Граница Поиска Двухсот Символов",
            email: "b12s140@mail.ru");
        using var client = HostClients.CreateTeacherClient(_factory);
        var search = new string('x', MaxSearchLength);

        // when: GET /students?search=<200 символов>.
        using var response = await client.GetAsync($"/api/v1/students?search={Uri.EscapeDataString(search)}");

        // then: 200 → обычный ответ 200 (страница PagedResult; студент под search
        // не подпадает — пустая выборка).
        var root = await ApiAssert.ReadOkJsonAsync(response);
        ApiAssert.HasExactlyProperties(root, "items", "total", "page", "pageSize");
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task TS140_Search_201Chars_Returns400WithSearchFieldError()
    {
        // given: сессия teacher; search из 201 символа.
        using var client = HostClients.CreateTeacherClient(_factory);
        var search = new string('x', MaxSearchLength + 1);

        // when: GET /students?search=<201 символ>.
        using var response = await client.GetAsync($"/api/v1/students?search={Uri.EscapeDataString(search)}");

        // then: 400 «Данные заполнены неверно» с errors.search=['Поиск — не более 200 символов'].
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var root = await ApiAssert.ReadJsonAsync(response);
        ApiAssert.HasExactlyProperties(root, "message", "errors");
        Assert.Equal(ErrorTexts.InvalidData, root.GetProperty("message").GetString());
        var errors = root.GetProperty("errors");
        ApiAssert.HasExactlyProperties(errors, "search");
        var searchErrors = errors.GetProperty("search");
        Assert.Equal(1, searchErrors.GetArrayLength());
        Assert.Equal(ErrorTexts.SearchLength, searchErrors[0].GetString());
    }
}
