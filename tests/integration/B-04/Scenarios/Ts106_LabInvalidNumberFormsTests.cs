using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-106 (P0, negative; FR-017) «Лабораторные: невалидные формы number — 400».
/// given: teacher; semester валиден; content/assignmentUrl/defenseRequired
///        валидны; пары свободны.
/// when:  POST с number='0'; затем '-3'; затем '1.5'; затем 'abc'.
/// then:  все четыре — 400, errors.number=
///        ['Номер должен быть положительным числом'] (number — целое &gt;0).
/// </summary>
public sealed class Ts106_LabInvalidNumberFormsTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("1.5")]
    [InlineData("abc")]
    public async Task TS106_PostLabWithInvalidNumberForm_ReturnsNumberError(string number)
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: POST /labs с невалидной формой number ('0' | '-3' | '1.5' | 'abc'),
        // прочие поля валидны.
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number,
            semester = 1,
            content = "Форма номера",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: 400, errors.number ровно из словарного текста.
        await ApiAssert.AssertSingleFieldErrorAsync(
            response, "number", "Номер должен быть положительным числом");
    }
}
