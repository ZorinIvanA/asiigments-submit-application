using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-143 (P1, boundary; FR-020) «Студенты: поиск ровно 200 символов — валиден».
/// given: сессия teacher.
/// when:  GET /students?search=&lt;ровно 200 символов&gt;
/// then:  200 (не 400; граница включительно); фильтр применён — пустая выборка
///        (ни один сид-студент не содержит 200-символьную строку).
/// </summary>
public sealed class Ts143_StudentsSearchExactly200ValidTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts143_StudentsSearchExactly200ValidTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SearchOfExactly200Chars_IsAcceptedNot400()
    {
        // given: teacher авторизован.
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: search ровно 200 символов.
        var search = Uri.EscapeDataString(new string('x', 200));
        using var response = await client.GetAsync($"/api/v1/students?search={search}");

        // then: 200 (не 400); фильтр применён — ни одна сид-запись не подходит.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
    }
}
