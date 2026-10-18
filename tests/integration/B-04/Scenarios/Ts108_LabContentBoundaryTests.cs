using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-108 (P1, boundary; FR-017) «Лабораторные: границы длины и пустота content».
/// given: teacher; semester/number валидны; пары свободны.
/// when:  POST с content длиной ровно 500; POST с длиной 501; POST с content='   '.
/// then:  500 — 201; 501 — 400 errors.content=['Содержание — от 1 до 500
///        символов']; '   ' — 400 errors.content=['Заполните поле']
///        (пустое после трима поле — required).
/// </summary>
public sealed class Ts108_LabContentBoundaryTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS108_PostLabContentExactly500Chars_IsCreated()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: content длиной ровно 500 символов.
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 4001,
            semester = 1,
            content = new string('x', 500),
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: граница включительно — 201.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task TS108_PostLabContent501Chars_ReturnsLengthError()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: content длиной 501 символ.
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 4002,
            semester = 1,
            content = new string('x', 501),
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: 400 errors.content=['Содержание — от 1 до 500 символов'].
        await ApiAssert.AssertSingleFieldErrorAsync(
            response, "content", "Содержание — от 1 до 500 символов");
    }

    [Fact]
    public async Task TS108_PostLabWhitespaceOnlyContent_ReturnsRequiredError()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: content из одних пробелов (пустое после трима — required).
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 4003,
            semester = 1,
            content = "   ",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: 400 errors.content=['Заполните поле'].
        await ApiAssert.AssertSingleFieldErrorAsync(response, "content", "Заполните поле");
    }
}
