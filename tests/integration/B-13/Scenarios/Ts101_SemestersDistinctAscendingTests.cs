using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-101 (P0, happy_path; FR-018) «semesters: distinct по возрастанию».
/// given: DI-сид — работы в семестрах 1 и 2; анонимный клиент
///        (аутентификации в приложении нет, эндпойнты публичны).
/// when: GET /api/v1/semesters.
/// then: 200 [1,2] (массив целых, distinct по возрастанию) — FR-018 AC
///       (проверка «анонимно — 401» удалена вместе с аутентификацией).
/// </summary>
public sealed class Ts101_SemestersDistinctAscendingTests(B13WebAppFactory factory) : IClassFixture<B13WebAppFactory>
{
    private readonly B13WebAppFactory _factory = factory;

    [Fact]
    public async Task TS101_Semesters_ReturnsDistinctAscendingIntArray()
    {
        // given: DI-сид — работы в семестрах 1 и 2 (в семестре 1 две работы —
        // distinct наблюдаем); анонимный клиент.
        B13Seed.AddLab(_factory, 1, 1);
        B13Seed.AddLab(_factory, 1, 2);
        B13Seed.AddLab(_factory, 2, 1);
        using var client = HostClients.Create(_factory);

        // when: GET /api/v1/semesters.
        using var response = await client.GetAsync("/api/v1/semesters");

        // then: 200 — массив целых [1, 2], distinct по возрастанию (FR-018 AC).
        var body = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        var semesters = body.EnumerateArray()
            .Select(item =>
            {
                Assert.Equal(JsonValueKind.Number, item.ValueKind);
                Assert.True(
                    item.TryGetInt32(out var semester),
                    "Элемент массива семестров — не целое число.");
                return semester;
            })
            .ToArray();
        Assert.Equal(new[] { 1, 2 }, semesters);
    }
}
