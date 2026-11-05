using System.Text.Json;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-164 «me/submissions: пользователь без группы — hasGroup:false»
/// (happy_path, P0, FR-021).
///
/// given: анонимный клиент; текущий пользователь — сид-преподаватель
///        (Seed__TeacherLogin), GroupId = null.
/// when:  GET /api/v1/me/submissions?semester=1.
/// then:  200 {hasGroup:false, labs:[], submissions:[]}
///        (FR-021 AC «Свои сдачи без группы»).
/// </summary>
public sealed class Ts164_MeSubmissionsNoGroupTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts164_MeSubmissionsNoGroupTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMeSubmissionsWithoutGroup_ReturnsEmptyHasGroupFalse()
    {
        // given: анонимный клиент; текущий пользователь — сид-преподаватель (без группы).
        Assert.Null(
            (await B06SubmissionsSessions.RequireUserAsync(_factory, SeedOptions.DefaultTeacherLogin)).GroupId);
        using var client = HostClients.Create(_factory);

        // when: GET /me/submissions?semester=1.
        using var response = await B06SubmissionsApi.GetMeSubmissionsAsync(client, semester: "1");

        // then: 200 {hasGroup:false, labs:[], submissions:[]}.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "hasGroup", "labs", "submissions");
        Assert.Equal(JsonValueKind.False, root.GetProperty("hasGroup").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("labs").ValueKind);
        Assert.Equal(0, root.GetProperty("labs").GetArrayLength());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("submissions").ValueKind);
        Assert.Equal(0, root.GetProperty("submissions").GetArrayLength());
    }
}
