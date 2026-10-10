using System.Text.Json;
using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-164 «me/submissions: студент без группы — hasGroup:false»
/// (happy_path, P0, FR-021).
///
/// given: student31 без группы (демо-набор сида); его сессия.
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
        // given: student31 без группы; его сессия.
        var student31 = B06SubmissionsSessions.RequireUser(_factory, "student31");
        Assert.Null(student31.GroupId);
        using var client = B06SubmissionsSessions.CreateStudentSessionByLogin(_factory, "student31");

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
