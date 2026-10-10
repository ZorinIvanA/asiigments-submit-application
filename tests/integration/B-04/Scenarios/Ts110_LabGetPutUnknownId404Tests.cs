using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-110 (P1, negative; FR-017) «Лабораторные: неизвестный id — 404 для GET
/// и PUT».
/// given: teacher; uuid, которого нет в хранилище.
/// when:  GET /labs/{uuid}; затем PUT /labs/{uuid} с валидным телом.
/// then:  оба — 404 'Лабораторная не найдена'.
/// </summary>
public sealed class Ts110_LabGetPutUnknownId404Tests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS110_GetAndPutUnknownLabId_Return404LabNotFound()
    {
        // given: uuid, которого нет в хранилище; teacher.
        using var client = MintedSessions.CreateTeacherClient(_factory);
        var unknownId = Guid.NewGuid();

        // when/then: GET /labs/{uuid} — 404 'Лабораторная не найдена'.
        using var get = await client.GetAsync($"/api/v1/labs/{unknownId}");
        await ApiAssert.AssertMessageAsync(get, HttpStatusCode.NotFound, "Лабораторная не найдена");

        // when/then: PUT /labs/{uuid} с валидным телом — тоже
        // 404 'Лабораторная не найдена'.
        using var put = await client.PutAsJsonAsync($"/api/v1/labs/{unknownId}", new
        {
            semester = 2,
            number = 40,
            content = "Валидное тело",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });
        await ApiAssert.AssertMessageAsync(put, HttpStatusCode.NotFound, "Лабораторная не найдена");
    }
}
