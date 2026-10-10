using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-123 «me/submissions: студент в группе — только свои сдачи»
/// (happy_path, FR-021).
///
/// given: student01 состоит в ИК-221 (демо-сид); в семестре 1 — 20 работ; у
///        student01 3 сид-сдачи (у student02 — своя, не должна попасть);
///        его валидная сессия.
/// when:  GET /api/v1/me/submissions?semester=1
/// then:  200 {hasGroup:true, labs:20 (number↑), submissions:3 — только
///        student01, элементы без поля studentId, без чужих строк} (FR-021 AC
///        «Свои сдачи в группе»).
/// </summary>
public sealed class Ts123_MySubmissionsOwnOnlyTests : IClassFixture<B05WebAppFactory>
{
    private const string MySubmissionsEndpoint = "/api/v1/me/submissions";

    private readonly B05WebAppFactory _factory;

    public Ts123_MySubmissionsOwnOnlyTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task StudentInGroup_GetsSemesterLabsAndOnlyOwnSubmissions()
    {
        // given: student01 в ИК-221; 20 работ семестра 1; 3 сид-сдачи student01.
        var student01 = B05SeedLookup.StudentByLogin(_factory, "student01");
        var ik221 = B05SeedLookup.GroupByName(_factory, "ИК-221");
        Assert.Equal(ik221.Id, student01.GroupId);

        var labs = _factory.Services.GetRequiredService<ILabRepository>().GetAll();
        var semester1LabIds = labs
            .Where(lab => lab.Semester == 1)
            .Select(lab => lab.Id)
            .ToHashSet();
        Assert.Equal(20, semester1LabIds.Count);

        var ownLabIds = labs
            .Where(lab => lab.Semester == 1 && lab.Number is 1 or 2 or 3)
            .Select(lab => lab.Id.ToString())
            .ToHashSet();

        using var client = B05MintedSessions.Create(_factory);
        B05MintedSessions.MintAccessCookie(_factory, client, student01.Id, UserRoles.Student);

        // when: свои сдачи по семестру 1.
        using var response = await client.GetAsync($"{MySubmissionsEndpoint}?semester=1");

        // then: 200 {hasGroup:true, labs:20 (number↑), submissions:3 только свои}.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "hasGroup", "labs", "submissions");
        Assert.True(root.GetProperty("hasGroup").GetBoolean());

        var labRows = root.GetProperty("labs").EnumerateArray().ToList();
        Assert.Equal(20, labRows.Count);
        Assert.Equal(
            Enumerable.Range(1, 20).ToList(),
            labRows.Select(item => item.GetProperty("number").GetInt32()).ToList());
        Assert.True(
            semester1LabIds.SetEquals(
                labRows.Select(item => Guid.Parse(item.GetProperty("id").GetString()!)).ToHashSet()),
            "Колонки своих сдач — не работы семестра 1.");

        var submissions = root.GetProperty("submissions").EnumerateArray().ToList();
        Assert.Equal(3, submissions.Count);
        var actualLabIds = new HashSet<string>();
        foreach (var item in submissions)
        {
            // Элементы сдач текущего студента — без поля studentId (он известен из сессии).
            BodyAssertions.HasExactlyProperties(item, "labId", "submitDate", "defenseDate");
            actualLabIds.Add(item.GetProperty("labId").GetString()!);
        }

        Assert.True(
            ownLabIds.SetEquals(actualLabIds),
            $"Ожидались сдачи только student01 (работы 1,2,3), фактически labId: [{string.Join("; ", actualLabIds)}].");
    }
}
