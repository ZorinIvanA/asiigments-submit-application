using System.Globalization;
using System.Text.Json;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;
using LabsApp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-101 «semesters: distinct по возрастанию» (happy_path, FR-018, P0).
///
/// given: анонимный клиент (аутентификации в приложении нет, эндпойнты публичны);
///        DI-сид — работы в семестрах 1 и 2.
/// when:  GET /api/v1/semesters.
/// then:  200 [1,2] (массив целых, по возрастанию). FR-018 AC
///        (проверка «анонимно — 401» удалена вместе с аутентификацией).
/// </summary>
public sealed class Ts101_SemestersDistinctAscendingTests : IClassFixture<B14WebAppFactory>
{
    private const string SemestersEndpoint = "/api/v1/semesters";

    private readonly B14WebAppFactory _factory;

    public Ts101_SemestersDistinctAscendingTests(B14WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Semesters_ReturnsDistinctAscendingIntArray()
    {
        // given: работы в семестрах 1 и 2 (в семестре 1 две работы — distinct
        // наблюдаем; DI-сид в ILabRepository, ADR-010); анонимный клиент.
        AddLab(semester: 1, number: 1);
        AddLab(semester: 1, number: 2);
        AddLab(semester: 2, number: 1);
        using var client = B14Harness.Create(_factory);

        // when: GET /api/v1/semesters.
        using var response = await client.GetAsync(SemestersEndpoint);

        // then: 200 — массив целых [1, 2], distinct по возрастанию (FR-018 AC).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var body = document.RootElement;
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

    /// <summary>DI-сид работы (ADR-010): прямая вставка в ILabRepository тестового хоста.</summary>
    private void AddLab(int semester, int number)
    {
        var lab = new Lab
        {
            Id = Guid.NewGuid(),
            Semester = semester,
            Number = number,
            Content = string.Create(CultureInfo.InvariantCulture, $"Содержание (DI-сид) {semester}:{number}"),
            AssignmentUrl = null,
            DefenseRequired = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _factory.Services.GetRequiredService<ILabRepository>().AddAsync(lab);
    }
}
