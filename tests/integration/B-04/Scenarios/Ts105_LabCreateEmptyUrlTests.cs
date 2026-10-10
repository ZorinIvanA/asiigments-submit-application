using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-105 (P0, happy_path; FR-017 AC «Создание») «Лабораторные: создание с пустой
/// ссылкой — нормализация к null».
/// given: запрос от teacher; пары (semester=1, number=21) не существует.
/// when:  POST /labs {number:21, semester:1, content:'Новая', assignmentUrl:'', defenseRequired:true}.
/// then:  201; LabDto с assignmentUrl=null, id≠null, content='Новая'
///        (нормализация: пустая ссылка → null).
/// </summary>
public sealed class Ts105_LabCreateEmptyUrlTests(B04LabsWebAppFactory factory) : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS105_PostLabWithEmptyAssignmentUrl_Creates201WithNullUrl()
    {
        var labs = _factory.Services.GetRequiredService<ILabRepository>();

        // given: пары (semester=1, number=21) не существует.
        Assert.Null(labs.TryGetByPair(semester: 1, number: 21));

        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: POST /labs с пустой ссылкой.
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 21,
            semester = 1,
            content = "Новая",
            assignmentUrl = "",
            defenseRequired = true,
        });

        // then: 201; LabDto с assignmentUrl=null, id≠null, content='Новая'.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var root = await ApiAssert.ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("assignmentUrl").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, root.GetProperty("id").ValueKind);
        Assert.False(string.IsNullOrEmpty(root.GetProperty("id").GetString()), "id работы пуст — given «id≠null» нарушен.");
        Assert.Equal("Новая", root.GetProperty("content").GetString());
    }
}
