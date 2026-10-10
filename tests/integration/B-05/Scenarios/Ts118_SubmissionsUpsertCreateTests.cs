using System.Globalization;
using System.Text;
using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-118 «submissions: upsert создаёт запись с updatedBy преподавателя»
/// (happy_path, FR-021).
///
/// given: пары (student05, работа семестра 1 №1) нет (проверено по шву
///        хранилища); сессия teacher (uuid преподавателя известен).
/// when:  PUT /api/v1/submissions {studentId:&lt;student05&gt;, labId:&lt;работа&gt;,
///        submitDate:'2026-09-20', defenseDate:null}
/// then:  200 Submission {id≠null, studentId, labId, submitDate:'2026-09-20',
///        defenseDate:null, updatedAt (ISO-8601), updatedBy=uuid преподавателя
///        сессии}; повторный GET ведомости отражает дату (FR-021 AC
///        «Upsert создаёт»).
/// </summary>
public sealed class Ts118_SubmissionsUpsertCreateTests : IClassFixture<B05WebAppFactory>
{
    private const string GridEndpoint = "/api/v1/submissions";

    private readonly B05WebAppFactory _factory;

    public Ts118_SubmissionsUpsertCreateTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UpsertNewPair_CreatesRecordWithSessionTeacherAndDateIsVisibleInGrid()
    {
        // given: пары (student05, работа семестра 1 №1) нет; uuid сид-работы.
        var student05 = B05SeedLookup.StudentByLogin(_factory, "student05");
        var lab = _factory.Services.GetRequiredService<ILabRepository>().TryGetByPair(1, 1)
            ?? throw new InvalidOperationException(
                "Работа 1:1 не найдена в демо-сиде — given кейса неисполним.");
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>();
        Assert.Null(submissions.GetByStudentAndLab(student05.Id, lab.Id));

        var teacher = B05SeedLookup.Teacher(_factory);
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: PUT /submissions с датой сдачи '2026-09-20' (защита null).
        using var response = await client.PutAsync(
            GridEndpoint,
            new StringContent(
                "{\"studentId\":\"" + student05.Id + "\",\"labId\":\"" + lab.Id + "\"," +
                "\"submitDate\":\"2026-09-20\",\"defenseDate\":null}",
                Encoding.UTF8,
                "application/json"));

        // then: 200 — полная запись Submission с updatedBy преподавателя сессии.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(
            root, "id", "studentId", "labId", "submitDate", "defenseDate", "updatedAt", "updatedBy");

        var createdId = root.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(createdId), "id записи сдачи пуст.");
        Assert.Equal(student05.Id.ToString(), root.GetProperty("studentId").GetString());
        Assert.Equal(lab.Id.ToString(), root.GetProperty("labId").GetString());
        Assert.Equal("2026-09-20", root.GetProperty("submitDate").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("defenseDate").ValueKind);

        var updatedAtRaw = root.GetProperty("updatedAt").GetString();
        Assert.False(string.IsNullOrWhiteSpace(updatedAtRaw), "updatedAt отсутствует.");
        Assert.True(
            DateTime.TryParse(updatedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt),
            $"updatedAt «{updatedAtRaw}» не разбирается как ISO-8601.");
        Assert.Equal(teacher.Id.ToString(), root.GetProperty("updatedBy").GetString());

        // then: повторный GET ведомости отражает дату (строка пары на одной из
        //       страниц студентов ИК-221).
        var foundOnPage = 0;
        for (var page = 1; page <= 5 && foundOnPage == 0; page++)
        {
            using var grid = await client.GetAsync(
                $"{GridEndpoint}?groupId={B05SeedLookup.GroupByName(_factory, "ИК-221").Id}&semester=1&page={page}");
            Assert.Equal(HttpStatusCode.OK, grid.StatusCode);
            var gridRoot = await BodyAssertions.ReadRootObjectAsync(grid);
            foreach (var row in gridRoot.GetProperty("submissions").EnumerateArray())
            {
                var rowStudent = row.GetProperty("studentId").GetString();
                var rowLab = row.GetProperty("labId").GetString();
                if (rowStudent == student05.Id.ToString() && rowLab == lab.Id.ToString())
                {
                    foundOnPage = page;
                    Assert.Equal("2026-09-20", row.GetProperty("submitDate").GetString());
                }
            }
        }

        Assert.True(
            foundOnPage > 0,
            "Повторный GET ведомости не отражает созданную сдачу (student05, работа 1:1).");
    }
}
