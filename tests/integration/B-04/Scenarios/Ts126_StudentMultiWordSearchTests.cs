using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-126 (P0, happy_path; FR-020 AC «Многословный поиск в одном поле») «Студенты:
/// многословный поиск в одном поле».
/// given: student01: fullName 'Иванов Иван Иванович 01', login 'student01',
///        email 'student01@example.com' (демо-сид); teacher.
/// when:  GET /api/v1/students?search=иван 01 (два токена, URL-encoded).
/// then:  200; student01 в выдаче (оба токена — подстроки без учёта регистра
///        ОДНОГО поля fullName); total соответствует числу подходящих записей.
/// </summary>
public sealed class Ts126_StudentMultiWordSearchTests(B04DemoSeedWebAppFactory factory)
    : IClassFixture<B04DemoSeedWebAppFactory>
{
    private readonly B04DemoSeedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS126_GetStudentsSearchBothTokensInFullName_FindsStudent01()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: многословный поиск «иван 01» (два токена, URL-encoded).
        var search = Uri.EscapeDataString("иван 01");
        using var response = await client.GetAsync($"/api/v1/students?search={search}");

        // then: 200; student01 в выдаче; total — числу подходящих записей
        // («иван» — подстрока fullName всех сид-студентов, «01» — только
        // у student01, поэтому подходит ровно одна запись).
        var root = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(1, root.GetProperty("total").GetInt32());

        var items = root.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(1, items.GetArrayLength());

        var student01 = items[0];
        Assert.Equal("student01", student01.GetProperty("login").GetString());
        Assert.Equal("Иванов Иван Иванович 01", student01.GetProperty("fullName").GetString());
        Assert.Equal("student01@example.com", student01.GetProperty("email").GetString());
    }
}
