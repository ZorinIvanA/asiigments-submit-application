using System.Globalization;
using System.Text;
using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-119 «submissions: upsert обновляет и сбрасывает даты» (happy_path, FR-021).
///
/// given: запись с submitDate='2026-09-01', defenseDate='2026-09-11' (сид-сдача
///        student01 по работе семестра 1 №1); сессия teacher.
/// when:  PUT /api/v1/submissions {studentId, labId, submitDate:null,
///        defenseDate:null}
/// then:  200; обе даты null (сброс); updatedAt обновлён; id записи прежний
///        (без дубля) (FR-021 AC «Upsert обновляет и сбрасывает»).
/// </summary>
public sealed class Ts119_SubmissionsUpsertResetDatesTests : IClassFixture<B05WebAppFactory>
{
    private const string GridEndpoint = "/api/v1/submissions";

    private readonly B05WebAppFactory _factory;

    public Ts119_SubmissionsUpsertResetDatesTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UpsertWithNullDates_ResetsBothAndKeepsRecordIdWithoutDuplicate()
    {
        // given: сид-запись student01 × работа 1:1 с датами 2026-09-01/2026-09-11.
        var student01 = B05SeedLookup.StudentByLogin(_factory, "student01");
        var lab = _factory.Services.GetRequiredService<ILabRepository>().TryGetByPair(1, 1)
            ?? throw new InvalidOperationException(
                "Работа 1:1 не найдена в демо-сиде — given кейса неисполним.");
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>();
        var seeded = submissions.GetByStudentAndLab(student01.Id, lab.Id)
            ?? throw new InvalidOperationException(
                "Сид-сдача student01 × 1:1 отсутствует — given кейса неисполним.");
        Assert.Equal(new DateOnly(2026, 9, 1), seeded.SubmitDate);
        Assert.Equal(new DateOnly(2026, 9, 11), seeded.DefenseDate);

        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // Снимок времени того же TimeProvider, что использует приложение,
        // непосредственно перед PUT (CR-002: без привязки к абсолютной метке сида
        // — недетерминизм от часов хоста устранён).
        var beforePut = _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        // when: PUT с обеими датами null (сброс).
        using var response = await client.PutAsync(
            GridEndpoint,
            new StringContent(
                "{\"studentId\":\"" + student01.Id + "\",\"labId\":\"" + lab.Id + "\"," +
                "\"submitDate\":null,\"defenseDate\":null}",
                Encoding.UTF8,
                "application/json"));

        // then: 200; обе даты null; id прежний; updatedAt обновлён (не ранее
        //       снимка перед PUT — сид-метка 2026-09-11 заведомо раньше).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(seeded.Id.ToString(), root.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("submitDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("defenseDate").ValueKind);

        var updatedAtRaw = root.GetProperty("updatedAt").GetString();
        Assert.False(string.IsNullOrWhiteSpace(updatedAtRaw), "updatedAt отсутствует.");
        Assert.True(
            DateTime.TryParse(updatedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt),
            $"updatedAt «{updatedAtRaw}» не разбирается как ISO-8601.");
        Assert.True(
            updatedAt >= beforePut,
            $"updatedAt «{updatedAtRaw}» не обновлён (снимок перед PUT {beforePut:O}).");

        // then: в хранилище та же запись (без дубля) с обеими сброшенными датами.
        var stored = submissions.GetByStudentAndLab(student01.Id, lab.Id);
        Assert.NotNull(stored);
        Assert.Equal(seeded.Id, stored!.Id);
        Assert.Null(stored.SubmitDate);
        Assert.Null(stored.DefenseDate);
    }
}
