using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-109 (P0, happy_path; FR-017 AC «Редактирование без самоконфликта»)
/// «Лабораторные: PUT собственной пары без самоконфликта».
/// given: лабораторная (semester=1, number=5) существует.
/// when:  PUT /labs/{id} той же записи с теми же (semester=1, number=5), но новым content.
/// then:  200; обновлённый LabDto (не 409 — собственная запись конфликтом не считается).
/// </summary>
public sealed class Ts109_LabPutNoSelfConflictTests(B04LabsWebAppFactory factory) : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS109_PutLabWithOwnPair_Updates200WithoutConflict()
    {
        var labs = _factory.Services.GetRequiredService<ILabRepository>();

        // given: лабораторная (semester=1, number=5) существует.
        var lab = B04DomainSeed.AddLab(labs, semester: 1, number: 5);

        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: PUT той же записи с той же парой и новым content.
        using var response = await client.PutAsJsonAsync($"/api/v1/labs/{lab.Id}", new
        {
            number = 5,
            semester = 1,
            content = "Обновлённое содержание",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: 200 (не 409); обновлённый LabDto той же записи.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var root = await ApiAssert.ReadJsonAsync(response);
        Assert.Equal(lab.Id.ToString(), root.GetProperty("id").GetString());
        Assert.Equal(1, root.GetProperty("semester").GetInt32());
        Assert.Equal(5, root.GetProperty("number").GetInt32());
        Assert.Equal("Обновлённое содержание", root.GetProperty("content").GetString());
    }
}
