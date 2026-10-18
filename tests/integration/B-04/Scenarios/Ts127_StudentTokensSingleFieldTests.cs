using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-127 (P0, negative; FR-020 AC «Токены в разных полях не складываются»)
/// «Студенты: токены в разных полях не складываются».
/// given: student01: fullName 'Иванов Иван Иванович 01', login 'student01',
///        email 'student01@example.com' (демо-сид); teacher.
/// when:  GET /students?search=иванов student01@example.com.
/// then:  200 с 0 записей ('иванов' — только в fullName, email — другое поле).
/// </summary>
public sealed class Ts127_StudentTokensSingleFieldTests(B04DemoSeedWebAppFactory factory)
    : IClassFixture<B04DemoSeedWebAppFactory>
{
    private readonly B04DemoSeedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS127_GetStudentsSearchTokensAcrossFields_ReturnsEmpty()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: токены из разных полей ('иванов' — fullName, email-адрес — email).
        var search = Uri.EscapeDataString("иванов student01@example.com");
        using var response = await client.GetAsync($"/api/v1/students?search={search}");

        // then: 200 с 0 записей — токены не складываются из разных полей.
        var root = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(0, root.GetProperty("total").GetInt32());

        var items = root.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(0, items.GetArrayLength());
    }
}
