using LabsApp.IntegrationTests.B01.Infrastructure;
using System.Text;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-158 «Конверт ошибок: некорректный JSON тела — 400» (FR-023, P1).
/// given: teacher авторизован.
/// when: POST /labs с телом '{bad json'.
/// then: 400 {message:'Данные заполнены неверно'} (MALFORMED_JSON:
/// «синтаксически некорректный JSON тела → 400», FR-023).
/// Примечание: кейс прежней нумерации контура (в текущем списке кейсов батча
/// B-01 не значится) — сохранён в зоне как регресс-покрытие конверта FR-023.
/// </summary>
public sealed class Ts158_ErrorEnvelopeMalformedJsonBusinessEndpointTests
{
    [Fact]
    public async Task PostLabsWithMalformedJson_ReturnsInvalidDataEnvelope()
    {
        // given: teacher авторизован — валидная access-cookie сеяного учителя
        // (ADR-015: DI-сид + минтованный access-JWT вместо HTTP-входа).
        using var factory = new B01WebAppFactory();
        using var client = MintedSession.CreateTeacherClient(factory);

        // when: POST /api/v1/labs (бизнес-эндпойнт) с синтаксически некорректным
        // JSON в теле.
        using var content = new StringContent("{bad json", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/labs", content);

        // then: 400 {'message':'Данные заполнены неверно'} — литерал словаря
        // замороженной спеки, не прод-константа (дрейф словаря должен ловиться
        // тестом); без errors (ErrorEnvelopeAsync проверяет точное множество
        // ключей тела).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await JsonAssert.ErrorEnvelopeAsync(response, "Данные заполнены неверно");
    }
}
