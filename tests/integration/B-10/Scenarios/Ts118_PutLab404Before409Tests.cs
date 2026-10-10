using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-118 (P1, negative; FR-032) «PUT /labs/{id}: 404 раньше 409 (СПО)».
/// given: Существует лаба (1,1) (DI-сид, ADR-010); U — случайный uuid.
/// when: PUT /labs/U {semester:1, number:1, content:'x', defenseRequired:false}
///       (валидное тело, пара занята другой записью).
/// then: 404 «Лабораторная не найдена» (не 409) — глоссарий «СПО»:
///       «404 (сущность) → 409 (уникальность)».
/// </summary>
public sealed class Ts118_PutLab404Before409Tests(B10NoDemoWebAppFactory factory) : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS118_PutOccupiedPairOnUnknownId_Returns404Not409()
    {
        // given: пара (1,1) занята существующей записью (DI-сид, ADR-010); U — случайный uuid.
        _ = B10Seed.AddLab(_factory, semester: 1, number: 1);
        using var client = HostClients.CreateTeacherClient(_factory);
        var unknownId = Guid.NewGuid();

        // when: валидное тело с занятой парой (1,1) на несуществующем id.
        using var response = await client.PutAsJsonAsync(
            $"/api/v1/labs/{unknownId}",
            new { semester = 1, number = 1, content = "x", defenseRequired = false });

        // then: 404 «Лабораторная не найдена», а не 409.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.NotFound,
            "Лабораторная не найдена",
            exactSingleMessageProperty: true);
    }
}
