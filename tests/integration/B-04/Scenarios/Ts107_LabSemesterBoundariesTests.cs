using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-107 (P0, boundary; FR-017 AC «Границы семестра») «Лабораторные: границы
/// семестра 1/10 и 0/11».
/// given: запрос от teacher; Labs__MaxSemester=10; пары свободны.
/// when:  POST с semester=1 и semester=10; затем отдельные POST с semester=0 и semester=11.
/// then:  первые два — 201; последние два — 400 с
///        errors.semester=['Семестр — число от 1 до 10'] (текст — шаблон от Labs__MaxSemester).
/// </summary>
public sealed class Ts107_LabSemesterBoundariesTests(B04LabsWebAppFactory factory) : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS107_PostLabSemesterBoundaryValues_Accepts1And10Rejects0And11()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when/then: semester=1 → 201; semester=10 → 201 (пары свободны — номера разные).
        using var semesterMin = await PostLabAsync(client, semester: 1, number: 31);
        Assert.Equal(HttpStatusCode.Created, semesterMin.StatusCode);

        using var semesterMax = await PostLabAsync(client, semester: 10, number: 32);
        Assert.Equal(HttpStatusCode.Created, semesterMax.StatusCode);

        // when/then: semester=0 и semester=11 → 400 с errors.semester ровно из одного текста.
        using var semesterZero = await PostLabAsync(client, semester: 0, number: 33);
        await ApiAssert.AssertSingleFieldErrorAsync(semesterZero, "semester", "Семестр — число от 1 до 10");

        using var semesterOver = await PostLabAsync(client, semester: 11, number: 34);
        await ApiAssert.AssertSingleFieldErrorAsync(semesterOver, "semester", "Семестр — число от 1 до 10");
    }

    private static Task<HttpResponseMessage> PostLabAsync(HttpClient client, int semester, int number) =>
        client.PostAsJsonAsync("/api/v1/labs", new
        {
            number,
            semester,
            content = "С",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });
}
