using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-116 (P0, negative; FR-032) «PUT /labs/{id}: неизвестный id → 404».
/// given: Случайный uuid, не совпадающий ни с одной лабой.
/// when: PUT /labs/&lt;uuid&gt; с валидным телом.
/// then: 404 {"message":"Лабораторная не найдена"} (FR-032 AC «Неизвестный id»).
/// </summary>
public sealed class Ts116_PutLabUnknownIdTests(B10NoDemoWebAppFactory factory) : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS116_PutUnknownLabId_Returns404NotFoundMessage()
    {
        // given: случайный uuid, не совпадающий ни с одной лабой; сессия teacher.
        using var client = HostClients.CreateTeacherClient(_factory);
        var unknownId = Guid.NewGuid();

        // when: PUT с валидным телом.
        using var response = await client.PutAsJsonAsync(
            $"/api/v1/labs/{unknownId}",
            new { semester = 1, number = 1, content = "Валидное содержание", assignmentUrl = (string?)null, defenseRequired = false });

        // then: 404 {"message":"Лабораторная не найдена"}.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.NotFound,
            "Лабораторная не найдена",
            exactSingleMessageProperty: true);
    }
}
