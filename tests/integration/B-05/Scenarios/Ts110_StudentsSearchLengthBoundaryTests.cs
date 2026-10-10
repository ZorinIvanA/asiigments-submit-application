using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-110 (P0, boundary; FR-020 AC «Поиск длиннее 200», FR-023) «Студенты:
/// граница длины поиска 200/201».
/// given: сессия teacher.
/// when:  GET /students?search=&lt;ровно 200 символов&gt;; GET /students?search=&lt;201 символ&gt;.
/// then:  первый — 200 (фильтр применяется); второй — 400 'Данные заполнены неверно'
///        + errors.search=['Поиск — не более 200 символов'].
/// </summary>
public sealed class Ts110_StudentsSearchLengthBoundaryTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts110_StudentsSearchLengthBoundaryTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SearchOfExactly200Chars_IsAcceptedAndFilterApplies()
    {
        // given: teacher авторизован.
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: search ровно 200 символов (ни один сид-студент не содержит её).
        var search = Uri.EscapeDataString(new string('x', 200));
        using var response = await client.GetAsync($"/api/v1/students?search={search}");

        // then: 200 (не 400); фильтр применён — пустая выборка (без фильтра были бы
        // все 32 сид-студента).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task SearchOf201Chars_IsRejectedWithFieldError()
    {
        // given: teacher авторизован.
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: search из 201 символа.
        var search = Uri.EscapeDataString(new string('и', 201));
        using var response = await client.GetAsync($"/api/v1/students?search={search}");

        // then: 400 'Данные заполнены неверно' + errors.search ровно из словарного текста.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        B05ContractAsserts.SingleFieldErrorIs(root, "search", "Поиск — не более 200 символов");
    }
}
