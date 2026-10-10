using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-118 (P1, negative; FR-017, словарь lab.url) «Лабораторные: assignmentUrl без
/// http(s)-префикса — 400».
/// given: teacher; прочие поля валидны.
/// when:  POST /labs с assignmentUrl='ftp://example.com/x'.
/// then:  400; errors.assignmentUrl=['Ссылка должна начинаться с http:// или https://'].
/// </summary>
public sealed class Ts118_LabUrlPrefixTests(B04LabsWebAppFactory factory) : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS118_PostLabWithNonHttpUrl_Returns400WithPrefixError()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: POST /labs со ссылкой без http(s)-префикса.
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 71,
            semester = 1,
            content = "С",
            assignmentUrl = "ftp://example.com/x",
            defenseRequired = false,
        });

        // then: 400; errors.assignmentUrl ровно из дословного текста словаря lab.url.
        await ApiAssert.AssertSingleFieldErrorAsync(
            response, "assignmentUrl", "Ссылка должна начинаться с http:// или https://");
    }
}
