using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-130 «Студенты: токены в разных полях не складываются» (negative, FR-020).
///
/// given: student01: fullName 'Иванов Иван Иванович 01', login 'student01',
///        email 'student01@example.com' (демо-сид FR-004); сессия teacher (минт, ADR-015).
/// when:  GET /api/v1/students?search=иванов student01@example.com
/// then:  200; total=0, items=[] — токен «иванов» встречается только в fullName,
///        а «student01@example.com» только в email; запись проходит, только если
///        ВСЕ токены — подстроки ОДНОГО И ТОГО ЖЕ поля (FR-020 AC «Токены в разных
///        полях не складываются»).
/// </summary>
public sealed class Ts130_StudentsCrossFieldTokensTests : IClassFixture<B05WebAppFactory>
{
    private const string StudentsEndpoint = "/api/v1/students";

    private readonly B05WebAppFactory _factory;

    public Ts130_StudentsCrossFieldTokensTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SearchTokensAcrossDifferentFields_MatchNothing()
    {
        // given: student01 существует в сиде; teacher авторизован (минт сессии).
        _ = B05SeedLookup.StudentByLogin(_factory, "student01");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: поиск двумя токенами из РАЗНЫХ полей student01.
        var search = Uri.EscapeDataString("иванов student01@example.com");
        using var response = await client.GetAsync($"{StudentsEndpoint}?search={search}");

        // then: 200; пустая выборка.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        var items = root.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(0, items.GetArrayLength());
    }
}
