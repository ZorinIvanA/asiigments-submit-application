using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-114 (P0, happy_path; FR-032) «PUT /labs/{id}: успех с сохранением пары и id».
/// given: Лаба (1,1) с id=X (DI-сид, ADR-010); сессия teacher.
/// when: PUT /api/v1/labs/X {semester:1, number:1, content:'Обновлено', assignmentUrl:null,
///       defenseRequired:false}.
/// then: 200 LabDto; id=X сохранён; content='Обновлено' (своя пара не конфликтует —
///       FR-032 AC «Успех с сохранением пары»).
/// </summary>
public sealed class Ts114_PutLabKeepOwnPairTests(B10NoDemoWebAppFactory factory) : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS114_PutOwnPair_Returns200LabDtoWithSameIdAndContent()
    {
        // given: DI-сид лабы (1,1); id=X — из сида (ADR-010); сессия teacher (минт, ADR-022).
        var labX = B10Seed.AddLab(_factory, semester: 1, number: 1).Id;
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: изменение с сохранением собственной пары (1,1).
        using var response = await client.PutAsJsonAsync(
            $"/api/v1/labs/{labX}",
            new { semester = 1, number = 1, content = "Обновлено", assignmentUrl = (string?)null, defenseRequired = false });

        // then: 200 LabDto; id=X сохранён; content='Обновлено'.
        var body = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(labX.ToString(), body.GetProperty("id").GetString());
        Assert.Equal("Обновлено", body.GetProperty("content").GetString());
    }
}
