using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-095 (P0, boundary; FR-017 AC «Граница длины assignmentUrl» + словарь lab.url)
/// «Лабораторные: границы assignmentUrl 1000/1001 и нарушение префикса».
/// given: сессия teacher; прочие поля валидны.
/// when:  POST /labs с assignmentUrl длиной ровно 1000 после трима (префикс https://);
///        длиной 1001; со значением 'ftp://example.com/x'.
/// then:  первый — 201; второй — 400,
///        errors.assignmentUrl=['Ссылка — не более 1000 символов']; третий — 400,
///        errors.assignmentUrl=['Ссылка должна начинаться с http:// или https://'].
/// </summary>
public sealed class Ts095_AssignmentUrlBoundaryAndPrefixTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS095_PostLabUrlBoundaries_1000Created_1001AndNonHttpRejected()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: assignmentUrl ровно 1000 символов после трима (префикс https://).
        var url1000 = "https://" + new string('a', 1000 - "https://".Length);
        Assert.Equal(1000, url1000.Length);
        using var ok = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 41,
            semester = 1,
            content = "С",
            assignmentUrl = url1000,
            defenseRequired = false,
        });

        // then: ровно 1000 — валидно (201).
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        // when: assignmentUrl 1001 символ.
        var url1001 = url1000 + "b";
        Assert.Equal(1001, url1001.Length);
        using var tooLong = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 42,
            semester = 1,
            content = "С",
            assignmentUrl = url1001,
            defenseRequired = false,
        });

        // then: 400 с серверным текстом словаря lab.url.length.
        await ApiAssert.AssertSingleFieldErrorAsync(tooLong, "assignmentUrl", "Ссылка — не более 1000 символов");

        // when: assignmentUrl без http(s)-префикса.
        using var wrongPrefix = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 71,
            semester = 1,
            content = "С",
            assignmentUrl = "ftp://example.com/x",
            defenseRequired = false,
        });

        // then: 400 с текстом словаря lab.url.
        await ApiAssert.AssertSingleFieldErrorAsync(
            wrongPrefix, "assignmentUrl", "Ссылка должна начинаться с http:// или https://");
    }
}
