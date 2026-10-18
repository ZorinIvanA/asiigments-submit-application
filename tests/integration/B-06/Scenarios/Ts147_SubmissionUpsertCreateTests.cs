using System.Globalization;
using System.Text.Json;
using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-147 «Upsert сдачи: создание записи» (happy_path, P0, FR-021).
///
/// given: пара (student05, работа 1.1) отсутствует (демо-сид: сид-сдачи — только
///        student01×(1.1..1.3) и student02×1.1); сессия teacher; uuid преподавателя известен.
/// when:  PUT /api/v1/submissions {studentId:&lt;student05&gt;, labId:&lt;1.1&gt;,
///        submitDate:'2026-09-20', defenseDate:null}; затем GET ведомости той
///        группы/семестра (ИК-221, семестр 1).
/// then:  200; тело Submission {id≠null, studentId, labId, submitDate:'2026-09-20',
///        defenseDate:null, updatedAt:ISO-8601, updatedBy:&lt;uuid преподавателя&gt;};
///        ведомость отражает дату (FR-021 AC «Upsert создаёт»).
/// </summary>
public sealed class Ts147_SubmissionUpsertCreateTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private const string SubmitDate = "2026-09-20";

    private const int GridPageSize = 5; // ведомость: страница студентов по 5 (FR-021).

    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly Guid _studentId;
    private readonly Guid _labId;
    private readonly Guid _groupId;

    public Ts147_SubmissionUpsertCreateTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        _studentId = B06SubmissionsSessions.RequireUser(factory, "student05").Id;
        _labId = B06SubmissionsSessions.RequireLab(factory, semester: 1, number: 1).Id;
        _groupId = B06SubmissionsSessions.RequireGroup(factory, "ИК-221").Id;
    }

    [Fact]
    public async Task PutSubmissionWithFreePair_CreatesRecordAndGridReflectsDate()
    {
        // given: пара (student05, работа 1.1) в хранилище отсутствует; сессия teacher.
        Assert.Null(_factory.Services.GetRequiredService<ISubmissionRepository>()
            .GetByStudentAndLab(_studentId, _labId));
        var (client, teacherId) = B06SubmissionsSessions.CreateTeacherSession(_factory);
        using var _client = client;

        // when: PUT /submissions {studentId, labId, submitDate:'2026-09-20', defenseDate:null}.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            _client, _studentId.ToString(), _labId.ToString(), SubmitDate, defenseDate: null);

        // then: 200 и полная запись Submission — поле в поле.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var submission = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(
            submission, "id", "studentId", "labId", "submitDate", "defenseDate", "updatedAt", "updatedBy");
        Assert.True(
            Guid.TryParse(submission.GetProperty("id").GetString(), out var id) && id != Guid.Empty,
            $"Ожидался непустой uuid в поле id, фактически: {submission.GetProperty("id").GetRawText()}");
        Assert.Equal(_studentId.ToString(), submission.GetProperty("studentId").GetString());
        Assert.Equal(_labId.ToString(), submission.GetProperty("labId").GetString());
        Assert.Equal(SubmitDate, submission.GetProperty("submitDate").GetString());
        Assert.Equal(JsonValueKind.Null, submission.GetProperty("defenseDate").ValueKind);
        Assert.Equal(teacherId, ParseGuidOrThrow(submission, "updatedBy"));
        AssertIso8601OrThrow(submission);

        // then: GET ведомости той группы/семестра отражает дату пары.
        Assert.True(
            await GridContainsPairRowWithDateAsync(_client),
            "GET /submissions не содержит строку пары (student05, работа 1.1) с submitDate '2026-09-20'.");
    }

    /// <summary>Обход всех страниц студентов ведомости: строка пары с датой из запроса.</summary>
    private async Task<bool> GridContainsPairRowWithDateAsync(HttpClient client)
    {
        using var first = await B06SubmissionsApi.GetGridAsync(
            client, _groupId.ToString(), semester: "1", page: "1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var grid = await BodyAssertions.ReadRootObjectAsync(first);
        var pageCount = (int)Math.Ceiling(grid.GetProperty("total").GetInt32() / (double)GridPageSize);
        if (PageContainsPairRowWithDate(grid))
        {
            return true;
        }

        for (var page = 2; page <= pageCount; page++)
        {
            using var response = await B06SubmissionsApi.GetGridAsync(
                client, _groupId.ToString(), semester: "1", page: page.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (PageContainsPairRowWithDate(await BodyAssertions.ReadRootObjectAsync(response)))
            {
                return true;
            }
        }

        return false;
    }

    private bool PageContainsPairRowWithDate(JsonElement grid)
    {
        foreach (var row in grid.GetProperty("submissions").EnumerateArray())
        {
            var studentId = row.GetProperty("studentId").GetString();
            var labId = row.GetProperty("labId").GetString();
            var submitDate = row.GetProperty("submitDate").GetString();
            if (Guid.TryParse(studentId, out var rowStudent)
                && Guid.TryParse(labId, out var rowLab)
                && rowStudent == _studentId
                && rowLab == _labId
                && string.Equals(submitDate, SubmitDate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Guid ParseGuidOrThrow(JsonElement submission, string fieldName)
    {
        var raw = submission.GetProperty(fieldName).GetString();
        Assert.True(
            Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty,
            $"Ожидался непустой uuid в поле {fieldName}, фактически: {submission.GetProperty(fieldName).GetRawText()}");
        return parsed;
    }

    private static void AssertIso8601OrThrow(JsonElement submission)
    {
        var raw = submission.GetProperty("updatedAt").GetString();
        Assert.False(
            string.IsNullOrEmpty(raw),
            $"Ожидалось непустое updatedAt (ISO-8601), фактически: {submission.GetProperty("updatedAt").GetRawText()}");
        Assert.True(
            DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            $"Ожидалось updatedAt в формате ISO-8601, фактически: «{raw}».");
    }
}
