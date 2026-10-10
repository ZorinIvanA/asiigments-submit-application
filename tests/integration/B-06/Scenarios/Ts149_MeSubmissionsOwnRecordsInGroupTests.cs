using System.Text.Json;
using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-149 «Свои сдачи: в группе — свои записи и работы семестра»
/// (happy_path, P0, FR-021).
///
/// given: student01 состоит в ИК-221; в семестре 1 — 20 работ;
///        у student01 3 сид-сдачи; есть чужие сдачи (student02).
/// when:  GET /me/submissions?semester=1 (student01).
/// then:  200 {hasGroup:true, labs:20 (number↑), submissions:3 — только свои
///        записи, без поля studentId и без чужих строк}
///        (AC FR-021 «Свои сдачи в группе»).
/// </summary>
public sealed class Ts149_MeSubmissionsOwnRecordsInGroupTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly Guid _groupId;
    private readonly Guid _lab11;
    private readonly Guid _lab12;
    private readonly Guid _lab13;

    public Ts149_MeSubmissionsOwnRecordsInGroupTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        _groupId = B06SubmissionsSessions.RequireGroup(factory, "ИК-221").Id;
        _lab11 = B06SubmissionsSessions.RequireLab(factory, 1, 1).Id;
        _lab12 = B06SubmissionsSessions.RequireLab(factory, 1, 2).Id;
        _lab13 = B06SubmissionsSessions.RequireLab(factory, 1, 3).Id;
    }

    [Fact]
    public async Task GetMeSubmissionsInGroup_ReturnsOnlyOwnSubmissionsWithoutStudentId()
    {
        // given: student01 состоит в ИК-221; 3 сид-сдачи student01; есть чужая сдача student02.
        var student01 = B06SubmissionsSessions.RequireUser(_factory, "student01");
        Assert.Equal(_groupId, student01.GroupId);
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>();
        Assert.Equal(3, submissions.ListByStudent(student01.Id).Count);
        Assert.True(
            submissions.ListByStudent(B06SubmissionsSessions.RequireUser(_factory, "student02").Id).Count > 0,
            "Шаг given «есть чужие сдачи (student02)» нарушен: сдач student02 в хранилище нет.");

        using var client = B06SubmissionsSessions.CreateStudentSessionByLogin(_factory, "student01");

        // when: GET /me/submissions?semester=1 (student01).
        using var response = await B06SubmissionsApi.GetMeSubmissionsAsync(client, semester: "1");

        // then: 200; hasGroup:true; labs — 20 работ семестра по number↑.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "hasGroup", "labs", "submissions");
        Assert.Equal(JsonValueKind.True, root.GetProperty("hasGroup").ValueKind);

        var labs = root.GetProperty("labs").EnumerateArray().ToList();
        Assert.Equal(20, labs.Count);
        var numbers = labs.Select(lab => lab.GetProperty("number").GetInt32()).ToList();
        Assert.True(
            numbers.SequenceEqual(numbers.OrderBy(number => number)),
            "Ожидались labs, упорядоченные по number по возрастанию.");

        // then: submissions — ровно 3 СВОИХ сид-сдачи, без поля studentId и без чужих строк.
        var ownSubmissions = root.GetProperty("submissions").EnumerateArray().ToList();
        Assert.Equal(3, ownSubmissions.Count);
        var expectedDates = new Dictionary<Guid, (string Submit, string? Defense)>
        {
            [_lab11] = ("2026-09-01", "2026-09-11"),
            [_lab12] = ("2026-09-02", "2026-09-12"),
            [_lab13] = ("2026-09-03", null),
        };
        foreach (var submission in ownSubmissions)
        {
            BodyAssertions.HasExactlyProperties(submission, "labId", "submitDate", "defenseDate");
            var labId = Guid.Parse(submission.GetProperty("labId").GetString()!);
            Assert.True(
                expectedDates.Remove(labId),
                $"Ожидалась сид-сдача student01 (1.1/1.2/1.3), фактически labId «{labId}» (чужая или лишняя строка).");
        }

        Assert.True(
            expectedDates.Count == 0,
            $"В submissions отсутствуют сид-сдачи student01: [{string.Join(", ", expectedDates.Keys)}].");
    }
}
