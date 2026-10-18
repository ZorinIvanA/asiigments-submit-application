using System.Text.Json;
using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-152 «me/submissions: студент без группы» (happy_path, P0, FR-021).
///
/// given: student31 без группы (демо-сид: student31/32 не включены в группы);
///        сессия student31.
/// when:  GET /api/v1/me/submissions?semester=1.
/// then:  200; {hasGroup:false, labs:[], submissions:[]}
///        (FR-021 AC «Свои сдачи без группы»).
/// </summary>
public sealed class Ts152_MeSubmissionsWithoutGroupTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts152_MeSubmissionsWithoutGroupTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMeSubmissionsWithoutGroup_ReturnsEmptyHasGroupFalse()
    {
        // given: сессия student31 (в демо-сиде студент без группы).
        using var client = B06SubmissionsSessions.CreateStudentSessionByLogin(_factory, "student31");
        Assert.True(
            B06SubmissionsSessions.RequireUser(_factory, "student31").GroupId is null,
            "Шаг given «student31 без группы» нарушен: в хранилище у студента есть группа.");

        // when: GET /me/submissions?semester=1.
        using var response = await B06SubmissionsApi.GetMeSubmissionsAsync(client, semester: "1");

        // then: 200 {hasGroup:false, labs:[], submissions:[]}.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "hasGroup", "labs", "submissions");
        Assert.Equal(JsonValueKind.False, root.GetProperty("hasGroup").ValueKind);
        Assert.Equal(0, root.GetProperty("labs").GetArrayLength());
        Assert.Equal(0, root.GetProperty("submissions").GetArrayLength());
    }
}
