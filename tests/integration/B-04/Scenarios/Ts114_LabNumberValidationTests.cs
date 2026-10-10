using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-114 (P1, negative; FR-017 «number — целое &gt;0») «Лабораторные: валидация
/// number — '0', '-3', '1.5', 'abc' невалидны».
/// given: teacher; прочие поля валидны.
/// when:  четыре отдельных POST /labs с number='0', number='-3', number='1.5', number='abc'.
/// then:  каждый — 400; errors.number=['Номер должен быть положительным числом'].
/// </summary>
public sealed class Ts114_LabNumberValidationTests(B04LabsWebAppFactory factory) : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("1.5")]
    [InlineData("abc")]
    public async Task TS114_PostLabWithInvalidNumber_Returns400WithNumberError(string number)
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: POST /labs с невалидным number (прочие поля валидны).
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number,
            semester = 1,
            content = "С",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: 400; errors.number ровно из дословного текста словаря.
        await ApiAssert.AssertSingleFieldErrorAsync(response, "number", "Номер должен быть положительным числом");
    }
}
