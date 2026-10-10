using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-119 (P0, happy_path; FR-033) «GET /labs/{id}: существующий и несуществующий».
/// given: Лаба создана (DI-сид: (1,1), ADR-010); U — случайный uuid; сессия teacher.
/// when: GET /labs/{id}; отдельно GET /labs/U.
/// then: 200 LabDto (поля по DTO-словарю: id, semester, number, content, assignmentUrl,
///       defenseRequired); 404 «Лабораторная не найдена». FR-033 AC.
/// </summary>
public sealed class Ts119_GetLabByIdTests(B10NoDemoWebAppFactory factory) : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS119_GetLabById_ExistingReturnsLabDto_UnknownReturns404()
    {
        // given: DI-сид существующей лабы (1,1); сессия teacher.
        var labX = B10Seed.AddLab(_factory, semester: 1, number: 1).Id;
        using var client = HostClients.CreateTeacherClient(_factory);

        // when/then: GET существующего id → 200 LabDto с точным набором полей DTO-словаря.
        using var found = await client.GetAsync($"/api/v1/labs/{labX}");
        var body = await ApiAssert.ReadOkJsonAsync(found);
        ApiAssert.HasExactlyProperties(body, "id", "semester", "number", "content", "assignmentUrl", "defenseRequired");
        Assert.Equal(labX.ToString(), body.GetProperty("id").GetString());
        Assert.Equal(1, body.GetProperty("semester").GetInt32());
        Assert.Equal(1, body.GetProperty("number").GetInt32());

        // when/then: GET случайного uuid → 404 «Лабораторная не найдена».
        using var missing = await client.GetAsync($"/api/v1/labs/{Guid.NewGuid()}");
        await ApiAssert.AssertMessageAsync(
            missing,
            HttpStatusCode.NotFound,
            "Лабораторная не найдена",
            exactSingleMessageProperty: true);
    }
}
