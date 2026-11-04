using System.Text.Json;
using LabsApp.Hosting.Configuration;
using LabsApp.Infrastructure;
using LabsApp.IntegrationTests.B06.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-148 «Свои сдачи: пользователь без группы — пустая структура»
/// (happy_path, P0, FR-021).
///
/// given: анонимный клиент; текущий пользователь — сид-преподаватель
///        (Seed__TeacherLogin), GroupId = null.
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
        // given: анонимный клиент; текущий пользователь — сид-преподаватель (без группы).
        var teacher = await B06SubmissionsSessions.RequireUserAsync(_factory, SeedOptions.DefaultTeacherLogin);
        Assert.Null(teacher.GroupId);
        using var client = HostClients.Create(_factory);

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
