using System.Text.Json;
using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-148 «Свои сдачи: студент без группы — пустая структура»
/// (happy_path, P0, FR-021).
///
/// given: student31 без группы; его сессия.
/// when:  GET /api/v1/me/submissions?semester=1.
/// then:  200 {hasGroup:false, labs:[], submissions:[]}
///        (AC FR-021 «Свои сдачи без группы»).
/// </summary>
public sealed class Ts148_MeSubmissionsStudentWithoutGroupTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts148_MeSubmissionsStudentWithoutGroupTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMeSubmissionsWithoutGroup_ReturnsEmptyHasGroupFalse()
    {
        // given: сессия student31 (демо-сид: студент без группы).
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
