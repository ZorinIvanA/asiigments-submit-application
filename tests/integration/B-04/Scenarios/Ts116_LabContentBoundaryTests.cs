using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-116 (P2, boundary; FR-017, словарь lab.content) «Лабораторные: границы
/// content 500/501 и пробельный content».
/// given: teacher; прочие поля валидны.
/// when:  POST с content длиной ровно 500; затем 501; затем content из одних пробелов.
/// then:  500 — 201; 501 — 400 с errors.content=['Содержание — от 1 до 500 символов'];
///        пробельный — 400 с errors.content=['Заполните поле'] (словарная конвенция:
///        трим до проверки, пустое после трима обязательное поле → required,
///        FieldValidators.BoundedText; гейты валидации labs в LabsApp.Tests зелёные).
/// </summary>
public sealed class Ts116_LabContentBoundaryTests(B04LabsWebAppFactory factory) : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS116_PostLabContentBoundaries_500Created501AndWhitespaceRejected()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when/then: content ровно 500 символов → 201.
        var content500 = new string('а', 500);
        Assert.Equal(500, content500.Length);
        using (var ok = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 61,
            semester = 1,
            content = content500,
            assignmentUrl = (string?)null,
            defenseRequired = false,
        }))
        {
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        // when/then: content 501 символ → 400 с текстом словаря lab.content.
        var content501 = content500 + "б";
        Assert.Equal(501, content501.Length);
        using (var tooLong = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 62,
            semester = 1,
            content = content501,
            assignmentUrl = (string?)null,
            defenseRequired = false,
        }))
        {
            await ApiAssert.AssertSingleFieldErrorAsync(tooLong, "content", "Содержание — от 1 до 500 символов");
        }

        // when/then: content из одних пробелов → 400 с required-текстом словаря
        // («Заполните поле»: трим до проверки, пустое после трима — required).
        using (var whitespace = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 63,
            semester = 1,
            content = "   ",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        }))
        {
            await ApiAssert.AssertSingleFieldErrorAsync(whitespace, "content", "Заполните поле");
        }
    }
}
