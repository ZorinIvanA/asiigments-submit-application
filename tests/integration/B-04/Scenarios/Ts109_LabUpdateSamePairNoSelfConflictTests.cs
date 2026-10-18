using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-109 (P1, happy_path; FR-017 AC «Редактирование без самоконфликта»)
/// «Лабораторные: update без самоконфликта».
/// given: лабораторная (semester=1, number=5) существует; teacher.
/// when:  PUT /labs/{id} той же записи с теми же (semester=1, number=5), но новым
///        content.
/// then:  200 — собственная запись конфликтом не считается (не 409).
/// </summary>
public sealed class Ts109_LabUpdateSamePairNoSelfConflictTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS109_PutLabWithOwnPair_Returns200Not409()
    {
        // given: лабораторная (semester=1, number=5) существует (DI-сид); teacher.
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        var lab = B04DomainSeed.AddLab(labs, semester: 1, number: 5);
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: PUT той же записи с той же парой (1,5) и новым content.
        using var response = await client.PutAsJsonAsync($"/api/v1/labs/{lab.Id}", new
        {
            semester = 1,
            number = 5,
            content = "Обновлённое содержание",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: 200 (не 409) — собственная пара конфликтом не считается.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var root = await ApiAssert.ReadJsonAsync(response);
        Assert.Equal(lab.Id.ToString(), root.GetProperty("id").GetString());
        Assert.Equal("Обновлённое содержание", root.GetProperty("content").GetString());
    }
}
