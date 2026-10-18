using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-108 (P0, negative; FR-020 AC «Токены в разных полях не складываются»)
/// «Студенты: токены в разных полях не складываются».
/// given: student01: fullName 'Иванов Иван Иванович 01', login 'student01',
///        email 'student01@example.com' (демо-сид FR-004); сессия teacher.
/// when:  GET /students?search=иванов student01@example.com
/// then:  200 с 0 записей ('иванов' — только в fullName, email — другое поле:
///        запись проходит, только если ВСЕ токены — подстроки ОДНОГО И ТОГО ЖЕ поля).
/// </summary>
public sealed class Ts108_StudentsCrossFieldTokensTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts108_StudentsCrossFieldTokensTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SearchTokensAcrossDifferentFields_ReturnsEmptySelection()
    {
        // given: student01 существует в сиде; teacher авторизован.
        _ = B05SeedLookup.StudentByLogin(_factory, "student01");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: поиск двумя токенами из РАЗНЫХ полей student01.
        var search = Uri.EscapeDataString("иванов student01@example.com");
        using var response = await client.GetAsync($"/api/v1/students?search={search}");

        // then: 200 с 0 записей.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
    }
}
