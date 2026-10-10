using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-107 (P1, boundary; FR-017) «Лабораторные: number строкой из цифр и
/// нормализация значений».
/// given: teacher; пара (1,30) свободна.
/// when:  POST /labs {number:'30' (строка цифр), semester:'1' (строка цифр),
///        content:'  Содержание  ', assignmentUrl:'  ', defenseRequired:true}.
/// then:  201; в ответе number=30, semester=1 (строки цифр приводятся к int),
///        content='Содержание' (трим), assignmentUrl=null (нормализация после
///        валидации).
/// </summary>
public sealed class Ts107_LabStringNumbersNormalizationTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS107_PostLabWithStringDigits_TrimsAndNormalizesToTypes()
    {
        var labs = _factory.Services.GetRequiredService<ILabRepository>();

        // given: пара (1,30) свободна.
        Assert.Null(labs.TryGetByPair(semester: 1, number: 30));

        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: POST /labs со строками цифр и необработанными строковыми полями.
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = "30",
            semester = "1",
            content = "  Содержание  ",
            assignmentUrl = "  ",
            defenseRequired = true,
        });

        // then: 201; number=30 и semester=1 — JSON-числа; content триммирован;
        // ссылка из одних пробелов нормализована в null.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var root = await ApiAssert.ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("number").ValueKind);
        Assert.Equal(30, root.GetProperty("number").GetInt32());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("semester").ValueKind);
        Assert.Equal(1, root.GetProperty("semester").GetInt32());
        Assert.Equal("Содержание", root.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("assignmentUrl").ValueKind);
    }
}
