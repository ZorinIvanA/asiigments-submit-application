using System.Globalization;
using System.Text.Json;
using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-143 «Сдачи: upsert создаёт запись с updatedBy преподавателя»
/// (happy_path, P0, FR-021).
///
/// given: пара (student05, работа семестра 1 №1) отсутствует; teacher-сессия
///        (uuid преподавателя известен тесту из хранилища хоста — ADR-015).
/// when:  PUT /api/v1/submissions {studentId:&lt;student05&gt;, labId:&lt;1.1&gt;,
///        submitDate:'2026-09-20', defenseDate:null}; затем GET ведомости
///        страницы student05.
/// then:  200; создана запись с updatedBy=uuid преподавателя и updatedAt
///        (ISO-8601, ≈now — часы хоста закреплены); повторный GET ведомости
///        отражает дату (FR-021 AC «Upsert создаёт»).
/// </summary>
public sealed class Ts143_SubmissionUpsertCreatesWithTeacherAttributionTests :
    IClassFixture<B06SubmissionsWebAppFactory>
{
    private const string SubmitDate = "2026-09-20";

    /// <summary>Допуск «updatedAt ≈ now» для закреплённых часов хоста (ADR-002).</summary>
    private static readonly TimeSpan NowTolerance = TimeSpan.FromMinutes(5);

    private const int GridPageSize = 5; // ведомость: страница студентов по 5 (FR-021).

    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly Guid _studentId;
    private readonly Guid _labId;
    private readonly Guid _groupId;

    public Ts143_SubmissionUpsertCreatesWithTeacherAttributionTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        _studentId = B06SubmissionsSessions.RequireUser(factory, "student05").Id;
        _labId = B06SubmissionsSessions.RequireLab(factory, semester: 1, number: 1).Id;
        _groupId = B06SubmissionsSessions.RequireGroup(factory, "ИК-221").Id;
    }

    [Fact]
    public async Task PutSubmissionOnFreePair_CreatesRecordWithTeacherAttribution()
    {
        // given: пара (student05, работа 1.1) в хранилище отсутствует; teacher-сессия.
        Assert.Null(_factory.Services.GetRequiredService<ISubmissionRepository>()
            .GetByStudentAndLab(_studentId, _labId));
        var (client, teacherId) = B06SubmissionsSessions.CreateTeacherSession(_factory);
        using var _client = client;
        var hostNow = _factory.Time.GetUtcNow();

        // when: PUT /submissions {studentId, labId, submitDate:'2026-09-20', defenseDate:null}.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            _client, _studentId.ToString(), _labId.ToString(), SubmitDate, defenseDate: null);

        // then: 200; созданная запись — updatedBy=uuid преподавателя, updatedAt ISO-8601 ≈now.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var submission = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(
            submission, "id", "studentId", "labId", "submitDate", "defenseDate", "updatedAt", "updatedBy");
        Assert.True(
            Guid.TryParse(submission.GetProperty("id").GetString(), out var createdId) && createdId != Guid.Empty,
            $"Ожидался непустой uuid в поле id, фактически: {submission.GetProperty("id").GetRawText()}");
        Assert.Equal(_studentId.ToString(), submission.GetProperty("studentId").GetString());
        Assert.Equal(_labId.ToString(), submission.GetProperty("labId").GetString());
        Assert.Equal(SubmitDate, submission.GetProperty("submitDate").GetString());
        Assert.Equal(JsonValueKind.Null, submission.GetProperty("defenseDate").ValueKind);
        Assert.True(
            Guid.TryParse(submission.GetProperty("updatedBy").GetString(), out var updatedBy)
            && updatedBy == teacherId,
            $"Ожидался updatedBy=uuid преподавателя ({teacherId}), фактически: {submission.GetProperty("updatedBy").GetRawText()}");

        var updatedAtRaw = submission.GetProperty("updatedAt").GetString();
        Assert.False(
            string.IsNullOrEmpty(updatedAtRaw),
            $"Ожидалось непустое updatedAt, фактически: {submission.GetProperty("updatedAt").GetRawText()}");
        Assert.True(
            DateTimeOffset.TryParse(updatedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt),
            $"Ожидалось updatedAt в формате ISO-8601, фактически: «{updatedAtRaw}».");
        Assert.True(
            (updatedAt - hostNow).Duration() <= NowTolerance,
            $"Ожидалось updatedAt ≈ now ({hostNow:O}), фактически: «{updatedAtRaw}».");

        // then: запись создана в хранилище (тот же id), а не только в ответе.
        var stored = _factory.Services.GetRequiredService<ISubmissionRepository>()
            .GetByStudentAndLab(_studentId, _labId);
        Assert.NotNull(stored);
        Assert.Equal(createdId, stored!.Id);

        // then: GET ведомости страницы student05 отражает дату пары.
        var studentPage = await ReadGridPageContainingStudentAsync(_client);
        Assert.True(
            PageContainsPairRowWithSubmitDate(studentPage),
            "GET ведомости страницы student05 не содержит строку пары (student05, работа 1.1) с submitDate '2026-09-20'.");
    }

    /// <summary>Ведомость: страница студентов, содержащая student05 (обход всех страниц).</summary>
    private async Task<JsonElement> ReadGridPageContainingStudentAsync(HttpClient client)
    {
        using var first = await B06SubmissionsApi.GetGridAsync(
            client, _groupId.ToString(), semester: "1", page: "1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var grid = await BodyAssertions.ReadRootObjectAsync(first);
        var pageCount = (int)Math.Ceiling(grid.GetProperty("total").GetInt32() / (double)GridPageSize);

        var found = default(JsonElement);
        var foundFlag = false;
        if (PageContainsStudent(grid))
        {
            return grid;
        }

        for (var page = 2; page <= pageCount && !foundFlag; page++)
        {
            using var response = await B06SubmissionsApi.GetGridAsync(
                client, _groupId.ToString(), semester: "1", page: page.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var candidate = await BodyAssertions.ReadRootObjectAsync(response);
            if (PageContainsStudent(candidate))
            {
                found = candidate;
                foundFlag = true;
            }
        }

        Assert.True(
            foundFlag,
            $"Студент student05 не найден ни на одной из {pageCount} страниц ведомости (шаг when «GET ведомости страницы student05» неисполним).");
        return found;
    }

    private bool PageContainsStudent(JsonElement grid)
    {
        foreach (var student in grid.GetProperty("students").EnumerateArray())
        {
            if (Guid.TryParse(student.GetProperty("id").GetString(), out var id) && id == _studentId)
            {
                return true;
            }
        }

        return false;
    }

    private bool PageContainsPairRowWithSubmitDate(JsonElement grid)
    {
        foreach (var row in grid.GetProperty("submissions").EnumerateArray())
        {
            var rowStudent = row.GetProperty("studentId").GetString();
            var rowLab = row.GetProperty("labId").GetString();
            var rowSubmitDate = row.GetProperty("submitDate").GetString();
            if (Guid.TryParse(rowStudent, out var studentId)
                && Guid.TryParse(rowLab, out var labId)
                && studentId == _studentId
                && labId == _labId
                && string.Equals(rowSubmitDate, SubmitDate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
