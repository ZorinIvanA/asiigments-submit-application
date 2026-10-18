using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-115 (P1, boundary; FR-017 «JSON-число или строка из цифр после трима»)
/// «Лабораторные: number строкой из цифр валиден».
/// given: teacher; пары (1, 22) не существует.
/// when:  POST /labs {number:' 22 ', semester:1, content:'С', assignmentUrl:null, defenseRequired:false}.
/// then:  201; LabDto.number=22 (int после трима строки из цифр).
/// </summary>
public sealed class Ts115_LabNumberStringTests(B04LabsWebAppFactory factory) : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS115_PostLabWithDigitStringNumber_Creates201WithIntNumber()
    {
        var labs = _factory.Services.GetRequiredService<ILabRepository>();

        // given: пары (semester=1, number=22) не существует.
        Assert.Null(labs.TryGetByPair(semester: 1, number: 22));

        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: POST /labs с number строкой из цифр с пробелами.
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = " 22 ",
            semester = 1,
            content = "С",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: 201; LabDto.number=22 — целое после трима строки из цифр.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var root = await ApiAssert.ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("number").ValueKind);
        Assert.Equal(22, root.GetProperty("number").GetInt32());
    }
}
