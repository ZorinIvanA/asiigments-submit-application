using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-132 «Студенты: поиск длиннее 200 — 400» (negative, FR-020).
///
/// given: запрос от teacher (минт сессии, ADR-015).
/// when:  GET /api/v1/students?search=&lt;строка из 201 символа&gt;
/// then:  400; message 'Данные заполнены неверно';
///        errors.search=['Поиск — не более 200 символов'] (SEARCH_TOO_LONG,
///        FR-020 AC «Поиск длиннее 200»).
/// </summary>
public sealed class Ts132_StudentsSearchTooLongTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts132_StudentsSearchTooLongTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SearchOf201Chars_IsRejectedWithFieldError()
    {
        // given: teacher авторизован.
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: search из 201 символа.
        var search = Uri.EscapeDataString(new string('и', 201));
        using var response = await client.GetAsync($"/api/v1/students?search={search}");

        // then: 400 с дословным конвертом ошибки поля search.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        B05ContractAsserts.SingleFieldErrorIs(root, "search", "Поиск — не более 200 символов");
    }
}
