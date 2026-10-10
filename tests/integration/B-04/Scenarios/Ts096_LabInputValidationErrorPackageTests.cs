using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-096 (P1, negative; FR-017 валидация LabInput, FR-023 единый конверт)
/// «Лабораторные: пакет ошибок валидации LabInput».
/// given: сессия teacher.
/// when:  POST /labs {number:'abc', semester:1, content:'x'×501 (строка из 501
///        символа 'x', без пробельного обрамления — длина после трима однозначна),
///        assignmentUrl:'ftp://x', defenseRequired:true}; отдельно POST с
///        number='0', number='-3', number='1.5'.
/// then:  первый — 400 'Данные заполнены неверно' с одновременными ошибками
///        errors.number=['Номер должен быть положительным числом'],
///        errors.content=['Содержание — от 1 до 500 символов'],
///        errors.assignmentUrl=['Ссылка должна начинаться с http:// или https://']
///        (semester=1 валиден — errors.semester нет); варианты number '0','-3','1.5' —
///        та же ошибка number (тексты — дословно из словаря «Текстов ошибок полей»).
/// </summary>
public sealed class Ts096_LabInputValidationErrorPackageTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS096_PostInvalidLabInput_ReturnsAllFieldErrorsAtOnce()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: POST /labs с пакетно невалидным телом (content — ровно 501 символ 'x').
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = "abc",
            semester = 1,
            content = new string('x', 501),
            assignmentUrl = "ftp://x",
            defenseRequired = true,
        });

        // then: 400 'Данные заполнены неверно' — конверт ровно {message, errors};
        // три полевых ошибки ОДНОВРЕМЕННО, каждый массив — ровно один словарный текст;
        // semester=1 валиден, поэтому errors.semester в конверте нет.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var root = await ApiAssert.ReadJsonAsync(response);
        Assert.Equal("Данные заполнены неверно", root.GetProperty("message").GetString());
        ApiAssert.HasExactlyProperties(root, "message", "errors");

        var errors = root.GetProperty("errors");
        ApiAssert.HasExactlyProperties(errors, "number", "content", "assignmentUrl");
        Assert.Equal(
            "Номер должен быть положительным числом", SingleErrorText(errors, "number"));
        Assert.Equal(
            "Содержание — от 1 до 500 символов", SingleErrorText(errors, "content"));
        Assert.Equal(
            "Ссылка должна начинаться с http:// или https://",
            SingleErrorText(errors, "assignmentUrl"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("1.5")]
    public async Task TS096_PostLabWithInvalidNumberVariants_ReturnsSameNumberError(string number)
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: POST /labs с number='0' | '-3' | '1.5' (прочие поля валидны).
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number,
            semester = 1,
            content = "С",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: та же ошибка number — 400, errors.number ровно из словарного текста.
        await ApiAssert.AssertSingleFieldErrorAsync(
            response, "number", "Номер должен быть положительным числом");
    }

    /// <summary>Единственный текст errors.{field} (массив ровно из одного элемента).</summary>
    private static string SingleErrorText(JsonElement errors, string field)
    {
        var texts = errors.GetProperty(field);
        Assert.Equal(1, texts.GetArrayLength());
        return texts[0].GetString()!;
    }
}
