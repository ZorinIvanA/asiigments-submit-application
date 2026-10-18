using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-098 (P1, happy_path; FR-017) «Лабораторные: GET по id — 200 LabDto / 404».
/// given: существует работа L (DI-сид, сессия teacher); несуществующий uuid известен.
/// when:  GET /labs/{L.id}; GET /labs/&lt;несуществующий-uuid&gt;.
/// then:  200 LabDto с полями id, semester, number, content, assignmentUrl,
///        defenseRequired; 404 'Лабораторная не найдена'.
/// </summary>
public sealed class Ts098_GetLabByIdTests(B04LabsWebAppFactory factory) : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS098_GetLabById_ReturnsLabDto_UnknownIdReturns404()
    {
        // given: работа L существует (DI-сид).
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        var lab = B04DomainSeed.AddLab(labs, semester: 2, number: 8);

        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: GET /labs/{L.id}.
        using var found = await client.GetAsync($"/api/v1/labs/{lab.Id}");

        // then: 200 LabDto — ровно поля id, semester, number, content,
        // assignmentUrl, defenseRequired со значениями записи.
        var root = await ApiAssert.ReadOkJsonAsync(found);
        ApiAssert.HasExactlyProperties(
            root, "id", "semester", "number", "content", "assignmentUrl", "defenseRequired");
        Assert.Equal(lab.Id.ToString(), root.GetProperty("id").GetString());
        Assert.Equal(2, root.GetProperty("semester").GetInt32());
        Assert.Equal(8, root.GetProperty("number").GetInt32());
        Assert.Equal(lab.Content, root.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("assignmentUrl").ValueKind);
        Assert.False(root.GetProperty("defenseRequired").GetBoolean());

        // when/then: GET по несуществующему uuid — 404 NOT_FOUND_LAB.
        using var missing = await client.GetAsync($"/api/v1/labs/{Guid.NewGuid()}");
        await ApiAssert.AssertMessageAsync(missing, HttpStatusCode.NotFound, "Лабораторная не найдена");
    }
}
