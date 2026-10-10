using System.Globalization;
using System.Text.Json;
using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-158 «Upsert сдачи: обновление и сброс обеих дат в null»
/// (happy_path, P0, FR-021).
///
/// given: запись с submitDate='2026-09-01', defenseDate='2026-09-11'
///        (сид-сдача student01 1.1 демо-набора; прежняя метка updatedAt —
///        фиксированная метка сида 2026-09-11 12:00 UTC, часы хоста закреплены
///        позже — 2026-09-20 10:00 UTC); сессия teacher.
/// when:  PUT /api/v1/submissions {studentId, labId, submitDate:null, defenseDate:null}.
/// then:  200; обе даты null (сброс); updatedAt обновлён;
///        updatedBy=uuid преподавателя (FR-021 AC «Upsert обновляет и сбрасывает»).
/// </summary>
public sealed class Ts158_SubmissionUpsertResetBothDatesTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly Guid _studentId;
    private readonly Guid _labId;

    public Ts158_SubmissionUpsertResetBothDatesTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        _studentId = B06SubmissionsSessions.RequireUser(factory, "student01").Id;
        _labId = B06SubmissionsSessions.RequireLab(factory, semester: 1, number: 1).Id;
    }

    [Fact]
    public async Task PutSubmissionWithNullDates_ResetsBothDatesAndUpdatesAttribution()
    {
        // given: сид-сдача student01×(1,1): submitDate '2026-09-01', defenseDate '2026-09-11'.
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>();
        var seedRecord = submissions.GetByStudentAndLab(_studentId, _labId);
        Assert.NotNull(seedRecord);
        Assert.Equal(new DateOnly(2026, 9, 1), seedRecord!.SubmitDate);
        Assert.Equal(new DateOnly(2026, 9, 11), seedRecord.DefenseDate);
        var previousUpdatedAt = seedRecord.UpdatedAt;

        var (client, teacherId) = B06SubmissionsSessions.CreateTeacherSession(_factory);
        using var _client = client;

        // when: PUT /submissions {studentId, labId, submitDate:null, defenseDate:null}.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            _client, _studentId.ToString(), _labId.ToString(), submitDate: null, defenseDate: null);

        // then: 200; обе даты null (сброс); updatedAt обновлён; updatedBy=uuid преподавателя.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var submission = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(JsonValueKind.Null, submission.GetProperty("submitDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, submission.GetProperty("defenseDate").ValueKind);

        var updatedAtRaw = submission.GetProperty("updatedAt").GetString();
        Assert.False(
            string.IsNullOrEmpty(updatedAtRaw),
            $"Ожидалось непустое updatedAt, фактически: {submission.GetProperty("updatedAt").GetRawText()}");
        Assert.True(
            DateTimeOffset.TryParse(updatedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt),
            $"Ожидалось updatedAt в формате ISO-8601, фактически: «{updatedAtRaw}».");
        Assert.True(
            updatedAt.UtcDateTime > previousUpdatedAt,
            $"Ожидалось updatedAt новее прежнего ({previousUpdatedAt:O}), фактически: «{updatedAtRaw}».");

        Assert.True(
            Guid.TryParse(submission.GetProperty("updatedBy").GetString(), out var updatedBy),
            $"Ожидался uuid преподавателя в updatedBy, фактически: {submission.GetProperty("updatedBy").GetRawText()}");
        Assert.Equal(teacherId, updatedBy);
    }
}
