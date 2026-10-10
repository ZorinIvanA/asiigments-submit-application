using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-117 (P0, negative; FR-032) «PUT /labs/{id}: 400 раньше 404».
/// given: Неизвестный uuid в маршруте.
/// when: PUT /labs/&lt;неизвестный-uuid&gt; с number=-1.
/// then: 400 (валидация прежде существования — FR-032 AC «400 раньше 404»;
///       СПО IF-001: 400 → 404 → 409).
/// </summary>
public sealed class Ts117_PutLabValidationBeforeExistenceTests(B10NoDemoWebAppFactory factory) : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS117_PutInvalidNumberOnUnknownId_Returns400Not404()
    {
        // given: неизвестный uuid в маршруте; сессия teacher.
        using var client = HostClients.CreateTeacherClient(_factory);
        var unknownId = Guid.NewGuid();

        // when: валидное тело, кроме number=-1.
        using var response = await client.PutAsJsonAsync(
            $"/api/v1/labs/{unknownId}",
            new { semester = 1, number = -1, content = "x", assignmentUrl = (string?)null, defenseRequired = false });

        // then: 400 (не 404) — полевая валидация раньше проверки существования.
        ApiAssert.AssertStatus(
            response,
            HttpStatusCode.BadRequest,
            "PUT /labs/<неизвестный-uuid> с number=-1");
    }
}
