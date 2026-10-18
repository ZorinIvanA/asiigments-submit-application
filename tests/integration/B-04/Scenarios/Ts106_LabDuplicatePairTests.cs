using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-106 (P0, negative; FR-017 AC «Дубликат пары») «Лабораторные: дубликат пары
/// (semester, number) — 409».
/// given: лабораторная (semester=1, number=1) существует.
/// when:  POST /labs {number:1, semester:1, content:'Дубль', assignmentUrl:null, defenseRequired:false}.
/// then:  409; message «Лабораторная с таким номером уже есть в семестре» (CONFLICT_LAB).
/// </summary>
public sealed class Ts106_LabDuplicatePairTests(B04LabsWebAppFactory factory) : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS106_PostLabWithExistingPair_Returns409ConflictMessage()
    {
        var labs = _factory.Services.GetRequiredService<ILabRepository>();

        // given: лабораторная (semester=1, number=1) существует.
        B04DomainSeed.AddLab(labs, semester: 1, number: 1);
        Assert.NotNull(labs.TryGetByPair(semester: 1, number: 1));

        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when: POST с той же парой (semester=1, number=1).
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 1,
            semester = 1,
            content = "Дубль",
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
