using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-115 (P0, negative; FR-032) «PUT /labs/{id}: конфликт с чужой парой → 409».
/// given: Лабы (1,1)=X и (1,2)=Y (DI-сид, ADR-010).
/// when: PUT /labs/X {semester:1, number:2, content:'x', defenseRequired:false}.
/// then: 409 «Лабораторная с таким номером уже есть в семестре»
///       (FR-032 AC «Конфликт с чужой парой»; текст — словарь IF-009 DUPLICATE_LAB).
/// </summary>
public sealed class Ts115_PutLabForeignPairConflictTests(B10NoDemoWebAppFactory factory) : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS115_PutPairOccupiedByOtherLab_Returns409DictionaryMessage()
    {
        // given: X — работа (1,1); Y — работа (1,2) (обе — DI-сид, ADR-010).
        var labX = B10Seed.AddLab(_factory, semester: 1, number: 1).Id;
        _ = B10Seed.AddLab(_factory, semester: 1, number: 2);
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: перевод X на пару (1,2), занятую другой записью.
        using var response = await client.PutAsJsonAsync(
            $"/api/v1/labs/{labX}",
            new { semester = 1, number = 2, content = "x", defenseRequired = false });

        // then: 409 с дословным текстом словаря.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Conflict,
            "Лабораторная с таким номером уже есть в семестре",
            exactSingleMessageProperty: true);
    }
}
