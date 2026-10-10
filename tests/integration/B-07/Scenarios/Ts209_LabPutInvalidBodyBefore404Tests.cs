using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-209 «Лабораторные: PUT с невалидным телом и несуществующим id — 400 раньше
/// 404» (negative, FR-017/FR-022, P1).
///
/// given: сессия teacher (минт access-JWT, ADR-015); записи с указанным uuid
///        не существует (Seed__DemoData=false — хранилище работ пусто); тело
///        запроса невалидно (semester=0).
/// when:  PUT /api/v1/labs/&lt;произвольный-uuid&gt; {number:1, semester:0,
///        content:'С', assignmentUrl:null, defenseRequired:false}.
/// then:  400; message 'Данные заполнены неверно';
///        errors.semester=['Семестр — число от 1 до 10'] (Labs__MaxSemester=10) —
///        НЕ 404: валидация LabInput выполняется раньше разрешения сущности
///        (порядок отказов 401→403→400→404→409, FR-017/FR-022).
/// </summary>
public sealed class Ts209_LabPutInvalidBodyBefore404Tests : IClassFixture<B07WebAppFactory>
{
    private readonly B07WebAppFactory _factory;

    public Ts209_LabPutInvalidBodyBefore404Tests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutWithInvalidBodyAndUnknownId_Returns400Before404()
    {
        // given: сессия teacher (минт, ADR-015); записи с uuid не существует.
        using var client = B07MintedSessions.CreateTeacherClient(_factory);

        // when: PUT /labs/<произвольный-uuid> с невалидным телом (semester=0).
        using var response = await client.PutAsJsonAsync($"/api/v1/labs/{Guid.NewGuid()}", new
        {
            number = 1,
            semester = 0,
            content = "С",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: 400 (НЕ 404) — валидация LabInput раньше разрешения сущности.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");

        // then: errors.semester — ['Семестр — число от 1 до 10'] дословно
        // (шаблон текста от Labs__MaxSemester=10).
        BodyAssertions.ErrorFieldEquals(root, "semester", "Семестр — число от 1 до 10");
    }
}
