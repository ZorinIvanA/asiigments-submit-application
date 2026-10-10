using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-103 (P0, negative; FR-017 AC «Дубликат пары») «Лабораторные: дубликат пары
/// (semester, number) — 409».
/// given: пара (1,1) существует; teacher.
/// when:  POST /labs {number:1, semester:1, content:'X', assignmentUrl:null,
///        defenseRequired:false}.
/// then:  409 'Лабораторная с таким номером уже есть в семестре'.
/// </summary>
public sealed class Ts103_LabDuplicatePair409Tests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS103_PostLabWithExistingPair_Returns409WithDuplicateText()
    {
        // given: пара (1,1) существует (DI-сид, ADR-010); teacher.
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        B04DomainSeed.AddLab(labs, semester: 1, number: 1);
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: POST /labs с занятой парой (semester=1, number=1).
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 1,
            semester = 1,
            content = "X",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: 409 'Лабораторная с таким номером уже есть в семестре'.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Conflict,
            "Лабораторная с таким номером уже есть в семестре");
    }
}
