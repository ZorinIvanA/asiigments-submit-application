using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-139 (P0, happy_path; FR-020 AC «Многословный поиск в одном поле»)
/// «Студенты: многословный поиск — все токены в ОДНОМ поле».
/// given: демо-сид: student01 'Иванов Иван Иванович 01'/'student01'/
///        'student01@example.com'; сессия teacher (минт, ADR-015).
/// when:  GET /api/v1/students?search=иван 01
/// then:  200; student01 в выдаче (оба токена — подстроки fullName без учёта
///        регистра, порядок токенов не значим); total корректен.
/// </summary>
public sealed class Ts139_StudentMultiWordSearchSingleFieldTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts139_StudentMultiWordSearchSingleFieldTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task MultiWordSearchBothTokensInFullName_FindsStudent01WithCorrectTotal()
    {
        // given: student01 существует в демо-сиде; teacher авторизован.
        _ = B05SeedLookup.StudentByLogin(_factory, "student01");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: многословный поиск «иван 01» (пробел — разделитель токенов).
        var search = Uri.EscapeDataString("иван 01");
        using var response = await client.GetAsync($"/api/v1/students?search={search}");

        // then: 200; student01 в выдаче; total=1 — «иван» есть в fullName всех
        // сид-студентов, «01» — только у student01 (оба токена — подстроки одного поля).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(1, root.GetProperty("total").GetInt32());

        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("student01", items[0].GetProperty("login").GetString());
        Assert.Equal("Иванов Иван Иванович 01", items[0].GetProperty("fullName").GetString());
        Assert.Equal("student01@example.com", items[0].GetProperty("email").GetString());
    }
}
