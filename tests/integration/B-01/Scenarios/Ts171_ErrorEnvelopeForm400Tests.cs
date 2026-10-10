using LabsApp.IntegrationTests.B01.Infrastructure;
using System.Text.Json;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-171 «Конверт ошибок: форма 400» (FR-023, P0).
/// given: teacher авторизован (валидный access).
/// when: POST /api/v1/labs с semester=0 (прочее валидно).
/// then: 400; Content-Type application/json; body.message='Данные заполнены
/// неверно'; body.errors.semester — массив строк ['Семестр — число от 1 до 10'];
/// errors присутствует только у 400 VALIDATION (тело — ровно ключи message+errors).
/// (В предыдущей нумерации зоны кейс значился как TS-155 — файл переименован
/// по актуальному набору кейсов батча.)
/// </summary>
public sealed class Ts171_ErrorEnvelopeForm400Tests
{
    /// <summary>Текст ошибки поля semester — словарь замороженной спеки
    /// («Тексты ошибок полей», Labs__MaxSemester=10 по умолчанию).</summary>
    private const string SemesterErrorText = "Семестр — число от 1 до 10";

    [Fact]
    public async Task PostLabsWithSemesterZero_ReturnsValidationEnvelopeWithStringArrayErrors()
    {
        // given: teacher авторизован — валидная access-cookie сеяного учителя
        // (ADR-015: DI-сид + минтованный access-JWT вместо HTTP-входа).
        using var factory = new B01WebAppFactory();
        using var client = MintedSession.CreateTeacherClient(factory);

        // when: POST /api/v1/labs с semester=0 (прочие поля валидны).
        using var response = await client.PostAsJsonAsync(
            "/api/v1/labs",
            new { semester = 0, number = 1, content = "Лабораторная работа" });

        // then: 400; Content-Type application/json.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        // then: body.message='Данные заполнены неверно' — литерал словаря
        // замороженной спеки (FR-023), не прод-константа (иначе дрейф словаря
        // от спеки тестом не ловится); errors присутствует только у 400
        // VALIDATION — ровно два ключа: message и errors.
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        JsonAssert.HasExactlyProperties(root, "message", "errors");
        Assert.Equal("Данные заполнены неверно", root.GetProperty("message").GetString());

        // then: body.errors.semester — массив строк ровно
        // ['Семестр — число от 1 до 10'] (дословный текст кейса).
        var semesterErrors = root.GetProperty("errors").GetProperty("semester");
        Assert.Equal(JsonValueKind.Array, semesterErrors.ValueKind);
        var texts = semesterErrors.EnumerateArray().ToList();
        var actualTexts = texts.Select(item => item.GetString()).ToList();
        Assert.True(
            actualTexts.Count == 1 && actualTexts[0] == SemesterErrorText,
            $"Ожидался массив errors.semester ровно ['{SemesterErrorText}'], "
            + $"фактически: [{string.Join(" | ", actualTexts)}]; тело: {body}");
    }
}
