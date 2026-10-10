using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-129 (P0, happy_path; FR-020 AC «Многословный поиск в одном поле») «Студенты:
/// многословный поиск — все токены в одном поле».
/// given: student01: fullName «Иванов Иван Иванович 01», login «student01»,
///        email «student01@example.com» (сид).
/// when:  GET /api/v1/students?search=иван 01 (teacher).
/// then:  200; student01 в выдаче (оба токена — подстроки fullName без учёта
///        регистра); total соответствует числу подходящих записей сида.
/// </summary>
public sealed class Ts129_StudentMultiWordSearchTests(B04DemoSeedWebAppFactory factory)
    : IClassFixture<B04DemoSeedWebAppFactory>
{
    private readonly B04DemoSeedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS129_GetStudentsSearchBothTokensInFullName_FindsStudent01()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: многословный поиск «иван 01» (пробел — разделитель токенов запроса).
        var search = Uri.EscapeDataString("иван 01");
        using var response = await client.GetAsync($"/api/v1/students?search={search}");

        // then: 200; student01 в выдаче; total — ровно одна подходящая запись сида
        // (оба токена — подстроки одного поля fullName только у student01: «иван»
        // есть в fullName всех 32 студентов, «01» — только у student01).
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
