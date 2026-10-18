using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-142 (P0, negative; FR-020 AC «Поиск длиннее 200») «Студенты: поиск длиннее
/// 200 символов — 400».
/// given: сессия teacher.
/// when:  GET /students?search=&lt;201 символ&gt;
/// then:  400 'Данные заполнены неверно' +
///        errors.search=['Поиск — не более 200 символов'].
/// </summary>
public sealed class Ts142_StudentsSearchOver200BadRequestTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts142_StudentsSearchOver200BadRequestTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SearchOf201Chars_IsRejectedWithDictionaryFieldError()
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
