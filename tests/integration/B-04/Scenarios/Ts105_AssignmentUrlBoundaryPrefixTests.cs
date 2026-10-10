using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-105 (P0, boundary; FR-017, ISS-015) «Лабораторные: границы и префикс
/// assignmentUrl (ISS-015)».
/// given: teacher; валидные прочие поля; пары свободны.
/// when:  POST с assignmentUrl длиной ровно 1000 после трима (префикс https://);
///        POST с длиной 1001; POST с 'ftp://example.com/x'.
/// then:  первый — 201; второй — 400 errors.assignmentUrl=
///        ['Ссылка — не более 1000 символов']; третий — 400
///        errors.assignmentUrl=['Ссылка должна начинаться с http:// или https://'].
/// </summary>
public sealed class Ts105_AssignmentUrlBoundaryPrefixTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS105_PostLabUrlExactly1000Chars_IsCreated()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: assignmentUrl = 'https://' (8) + 'x'×992 = ровно 1000 символов.
        var url = "https://" + new string('x', 992);
        Assert.Equal(1000, url.Length);

        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 3001,
            semester = 1,
            content = "Граница длины ссылки",
            assignmentUrl = url,
            defenseRequired = false,
        });

        // then: ровно 1000 символов после трима — валидно, 201.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task TS105_PostLabUrl1001Chars_ReturnsLengthError()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: assignmentUrl длиной 1001 символ после трима.
        var url = "https://" + new string('x', 993);
        Assert.Equal(1001, url.Length);

        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 3002,
            semester = 1,
            content = "Граница длины ссылки",
            assignmentUrl = url,
            defenseRequired = false,
        });

        // then: 400 errors.assignmentUrl=['Ссылка — не более 1000 символов'].
        await ApiAssert.AssertSingleFieldErrorAsync(
            response, "assignmentUrl", "Ссылка — не более 1000 символов");
    }

    [Fact]
    public async Task TS105_PostLabFtpUrl_ReturnsPrefixError()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: assignmentUrl с префиксом вне словаря (ftp://).
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 3003,
            semester = 1,
            content = "Префикс ссылки",
            assignmentUrl = "ftp://example.com/x",
            defenseRequired = false,
        });

        // then: 400 errors.assignmentUrl=
        // ['Ссылка должна начинаться с http:// или https://'].
        await ApiAssert.AssertSingleFieldErrorAsync(
            response,
            "assignmentUrl",
            "Ссылка должна начинаться с http:// или https://");
    }
}
