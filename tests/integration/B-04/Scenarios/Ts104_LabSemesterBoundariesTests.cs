using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-104 (P0, boundary; FR-017, ISS-015) «Лабораторные: границы семестра 1/10
/// и 0/11».
/// given: teacher; Labs__MaxSemester=10 (умолчание хоста зоны); прочие поля
///        валидны; пары свободны.
/// when:  POST с semester=1; POST с semester=10; POST с semester=0; POST
///        с semester=11.
/// then:  первые два — 201; последние два — 400 с errors.semester=
///        ['Семестр — число от 1 до 10'] — текст-шаблон от Labs__MaxSemester,
///        при N=10 дословно равен клиентскому.
/// </summary>
public sealed class Ts104_LabSemesterBoundariesTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Theory]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(0, false)]
    [InlineData(11, false)]
    public async Task TS104_PostLabSemesterBoundaries(int semester, bool withinRange)
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: POST /labs с граничным semester; number подобран так, что пара
        // (semester, number) уникальна и свободна для каждого варианта.
        var number = 100 + semester;
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number,
            semester,
            content = "Граница семестра",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: semester 1 и 10 — 201; semester 0 и 11 — 400 с errors.semester
        // ровно из словарного текста-шаблона при N=10.
        if (withinRange)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        else
        {
            await ApiAssert.AssertSingleFieldErrorAsync(
                response, "semester", "Семестр — число от 1 до 10");
        }
    }
}
