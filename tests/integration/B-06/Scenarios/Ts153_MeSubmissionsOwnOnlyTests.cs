using System.Text.Json;
using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-153 «me/submissions: студент в группе видит только свои сдачи» (happy_path, P0, FR-021).
///
/// given: student01 состоит в ИК-221 (демо-сид); в семестре 1 — 20 работ;
///        3 сид-сдачи student01 (1.1: 2026-09-01/2026-09-11; 1.2: 2026-09-02/2026-09-12;
///        1.3: 2026-09-03/null).
/// when:  GET /api/v1/me/submissions?semester=1 (student01).
/// then:  200; {hasGroup:true, labs:20 (number↑), submissions:3 сид-сдачи student01
///        без чужих строк}; элементы submissions не содержат studentId
///        (FR-021 AC «Свои сдачи в группе»).
/// </summary>
public sealed class Ts153_MeSubmissionsOwnOnlyTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly Guid _lab11;
    private readonly Guid _lab12;
    private readonly Guid _lab13;

    public Ts153_MeSubmissionsOwnOnlyTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        _lab11 = B06SubmissionsSessions.RequireLab(factory, 1, 1).Id;
        _lab12 = B06SubmissionsSessions.RequireLab(factory, 1, 2).Id;
        _lab13 = B06SubmissionsSessions.RequireLab(factory, 1, 3).Id;
    }

    [Fact]
    public async Task GetMeSubmissionsInGroup_ReturnsOnlyOwnSubmissionsWithoutStudentId()
    {
        // given: сессия student01, состоящего в ИК-221 (демо-сид).
        using var client = B06SubmissionsSessions.CreateStudentSessionByLogin(_factory, "student01");

        // when: GET /me/submissions?semester=1.
        using var response = await B06SubmissionsApi.GetMeSubmissionsAsync(client, semester: "1");

        // then: 200; hasGroup:true; labs:20 по number↑.
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

        // then: submissions — ровно 3 сид-сдачи student01, без чужих строк и без studentId.
        var submissions = root.GetProperty("submissions").EnumerateArray().ToList();
        Assert.Equal(3, submissions.Count);
        var expectedDates = new Dictionary<Guid, (string Submit, string? Defense)>
        {
            [_lab11] = ("2026-09-01", "2026-09-11"),
            [_lab12] = ("2026-09-02", "2026-09-12"),
            [_lab13] = ("2026-09-03", null),
        };
        foreach (var submission in submissions)
        {
            BodyAssertions.HasExactlyProperties(submission, "labId", "submitDate", "defenseDate");
            var labId = Guid.Parse(submission.GetProperty("labId").GetString()!);
            Assert.True(
                expectedDates.Remove(labId),
                $"Ожидалась сид-сдача student01 (1.1/1.2/1.3), фактически labId «{labId}».");
        }

        Assert.True(
            expectedDates.Count == 0,
            $"В submissions отсутствуют сид-сдачи student01: [{string.Join(", ", expectedDates.Keys)}].");
    }
}
