using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-101 (P0, happy_path; FR-018) «semesters: distinct по возрастанию; анонимно 401».
/// given: Сид: работы в семестрах 1 и 2; сессия student; анонимный запрос.
/// when: GET /api/v1/semesters под student; анонимно.
/// then: Под student — 200 [1,2] (массив целых, по возрастанию); анонимно — 401
///       «Не авторизован» (FR-018 AC).
/// </summary>
public sealed class Ts101_SemestersDistinctAscendingAuthTests(B13WebAppFactory factory) : IClassFixture<B13WebAppFactory>
{
    private readonly B13WebAppFactory _factory = factory;

    [Fact]
    public async Task TS101_Semesters_StudentSession_ReturnsDistinctAscendingIntArray()
    {
        // given: DI-сид — работы в семестрах 1 и 2 (в семестре 1 две работы —
        // distinct наблюдаем); сессия student (минт access-JWT, ADR-022).
        B13Seed.AddLab(_factory, 1, 1);
        B13Seed.AddLab(_factory, 1, 2);
        B13Seed.AddLab(_factory, 2, 1);
        B13Seed.AddStudent(_factory, "ts101-student", "Сидоров", groupId: null);
        using var client = HostClients.CreateStudentClient(_factory, "ts101-student");

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

    [Fact]
    public async Task TS101_Semesters_AnonymousRequest_Returns401Unauthorized()
    {
        // given: анонимный запрос (без cookie access_token).
        using var client = HostClients.Create(_factory);

        // when: GET /api/v1/semesters.
        using var response = await client.GetAsync("/api/v1/semesters");

        // then: 401 «Не авторизован» (FR-018 AC).
        await ApiAssert.AssertMessageAsync(response, HttpStatusCode.Unauthorized, ErrorTexts.Unauthorized);
    }
}
