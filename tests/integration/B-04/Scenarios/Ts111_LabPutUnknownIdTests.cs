using LabsApp.IntegrationTests.B04.Infrastructure;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-111 (P0, negative; FR-017 «несуществующий id → 404») «Лабораторные: PUT
/// несуществующего id — 404».
/// given: запись с указанным uuid не существует; тело валидно.
/// when:  PUT /labs/&lt;произвольный-uuid&gt;.
/// then:  404; message «Лабораторная не найдена» (NOT_FOUND_LAB).
/// </summary>
public sealed class Ts111_LabPutUnknownIdTests(B04LabsWebAppFactory factory) : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS111_PutLabWithUnknownId_Returns404NotFoundMessage()
    {
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: PUT по uuid, которого нет в хранилище, с валидным телом.
        using var response = await client.PutAsJsonAsync($"/api/v1/labs/{Guid.NewGuid()}", new
        {
            number = 9,
            semester = 1,
            content = "Валидное тело",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: 404 с дословным текстом NOT_FOUND_LAB.
        await ApiAssert.AssertMessageAsync(response, HttpStatusCode.NotFound, "Лабораторная не найдена");
    }
}
