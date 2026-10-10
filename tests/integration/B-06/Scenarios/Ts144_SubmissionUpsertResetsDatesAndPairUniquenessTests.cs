using System.Globalization;
using System.Text.Json;
using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-144 «Сдачи: upsert обновляет и сбрасывает даты; дублей нет»
/// (happy_path, P0, FR-021).
///
/// given: запись с submitDate='2026-09-01', defenseDate='2026-09-11'
///        существует (сид-сдача student01×1.1; прежняя updatedAt — фиксированная
///        метка сида 2026-09-11 12:00 UTC, часы хоста закреплены позже).
/// when:  PUT /submissions {та же пара, submitDate:null, defenseDate:null};
///        затем повторный PUT с теми же значениями.
/// then:  200; обе даты null (сброс); updatedAt обновлён (новее прежнего);
///        повторный PUT не создаёт вторую запись — по-прежнему одна на пару
///        (studentId,labId) (AC FR-021 «Upsert обновляет и сбрасывает»;
///        уникальность пары).
/// </summary>
public sealed class Ts144_SubmissionUpsertResetsDatesAndPairUniquenessTests :
    IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly Guid _studentId;
    private readonly Guid _labId;

    public Ts144_SubmissionUpsertResetsDatesAndPairUniquenessTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        _studentId = B06SubmissionsSessions.RequireUser(factory, "student01").Id;
        _labId = B06SubmissionsSessions.RequireLab(factory, semester: 1, number: 1).Id;
    }

    [Fact]
    public async Task RepeatedPutWithNullDates_ResetsDatesWithoutCreatingDuplicate()
    {
        // given: запись пары с submitDate='2026-09-01', defenseDate='2026-09-11' существует.
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>();
        var given = submissions.GetByStudentAndLab(_studentId, _labId);
        Assert.NotNull(given);
        Assert.Equal(new DateOnly(2026, 9, 1), given!.SubmitDate);
        Assert.Equal(new DateOnly(2026, 9, 11), given.DefenseDate);
        var previousUpdatedAt = given.UpdatedAt;
        var givenId = given.Id;

        var (client, _) = B06SubmissionsSessions.CreateTeacherSession(_factory);
        using var _client = client;

        // when: PUT /submissions {та же пара, submitDate:null, defenseDate:null}.
        using var first = await B06SubmissionsApi.PutSubmissionAsync(
            _client, _studentId.ToString(), _labId.ToString(), submitDate: null, defenseDate: null);

        // then: 200; обе даты null (сброс); updatedAt обновлён (новее прежнего).
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await BodyAssertions.ReadRootObjectAsync(first);
        Assert.Equal(JsonValueKind.Null, firstBody.GetProperty("submitDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, firstBody.GetProperty("defenseDate").ValueKind);
        var firstUpdatedAt = ParseIso8601OrThrow(firstBody, "updatedAt");
        Assert.True(
            firstUpdatedAt > previousUpdatedAt,
            $"Ожидалось updatedAt новее прежнего ({previousUpdatedAt:O}), фактически: {firstUpdatedAt:O}.");

        // when: повторный PUT с теми же значениями.
        using var second = await B06SubmissionsApi.PutSubmissionAsync(
            _client, _studentId.ToString(), _labId.ToString(), submitDate: null, defenseDate: null);

        // then: 200; даты по-прежнему null; вторая запись НЕ создана — ровно одна на пару.
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await BodyAssertions.ReadRootObjectAsync(second);
        Assert.Equal(JsonValueKind.Null, secondBody.GetProperty("submitDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, secondBody.GetProperty("defenseDate").ValueKind);

        var pairRecords = submissions.GetByPairs(
            new[] { _studentId }, new[] { _labId });
        Assert.True(
            pairRecords.Count == 1,
            $"Ожидалась ровно одна запись на пару (studentId,labId), фактически: {pairRecords.Count}.");
        Assert.Equal(givenId, pairRecords[0].Id);
        Assert.Null(pairRecords[0].SubmitDate);
        Assert.Null(pairRecords[0].DefenseDate);
    }

    private static DateTime ParseIso8601OrThrow(JsonElement submission, string fieldName)
    {
        var raw = submission.GetProperty(fieldName).GetString();
        Assert.False(
            string.IsNullOrEmpty(raw),
            $"Ожидалось непустое {fieldName} (ISO-8601), фактически: {submission.GetProperty(fieldName).GetRawText()}");
        Assert.True(
            DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed),
            $"Ожидалось {fieldName} в формате ISO-8601, фактически: «{raw}».");
        return parsed.UtcDateTime;
    }
}
