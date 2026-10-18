using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-108 (P0, boundary; FR-017 AC «Граница длины assignmentUrl», ISS-015)
/// «Лабораторные: граница длины assignmentUrl 1000/1001».
/// given: teacher; прочие поля валидны.
/// when:  POST с assignmentUrl длиной ровно 1000 после трима (префикс https://);
///        затем длиной 1001.
/// then:  первый — 201; второй — 400,
///        errors.assignmentUrl=['Ссылка — не более 1000 символов'] (серверное дополнение словаря).
/// </summary>
public sealed class Ts108_AssignmentUrlLengthBoundaryTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS108_PostLabAssignmentUrlExactly1000Created1001Rejected()
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

        // then: 400 с серверным текстом lab.url.length.
        await ApiAssert.AssertSingleFieldErrorAsync(tooLong, "assignmentUrl", "Ссылка — не более 1000 символов");
    }
}
