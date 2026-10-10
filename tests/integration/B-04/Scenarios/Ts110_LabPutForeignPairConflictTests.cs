using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-110 (P1, negative; FR-017 «409 при update — чужая пара») «Лабораторные: PUT
/// на пару, занятую другой записью — 409».
/// given: существуют записи (1,5) и (1,6).
/// when:  PUT /labs/{id записи (1,6)} с (semester=1, number=5).
/// then:  409 «Лабораторная с таким номером уже есть в семестре» (конфликт с чужой записью).
/// </summary>
public sealed class Ts110_LabPutForeignPairConflictTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS110_PutLabOntoForeignPair_Returns409ConflictMessage()
    {
        var labs = _factory.Services.GetRequiredService<ILabRepository>();

        // given: существуют записи (1,5) и (1,6).
        B04DomainSeed.AddLab(labs, semester: 1, number: 5);
        var record = B04DomainSeed.AddLab(labs, semester: 1, number: 6);

        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: PUT записи (1,6) на пару (1,5), занятую другой записью.
        using var response = await client.PutAsJsonAsync($"/api/v1/labs/{record.Id}", new
        {
            number = 5,
            semester = 1,
            content = "Перенос на чужую пару",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: 409 с дословным текстом CONFLICT_LAB.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Conflict,
            "Лабораторная с таким номером уже есть в семестре");
    }
}
