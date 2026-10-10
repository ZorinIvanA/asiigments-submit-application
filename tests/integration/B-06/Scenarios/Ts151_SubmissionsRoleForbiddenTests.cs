using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-151 «Сдачи: роли — student на ведомость и teacher на свои сдачи — 403»
/// (negative, P0, FR-021/FR-022).
///
/// given: существуют валидные сессии student01 и teacher.
/// when:  GET /submissions под student; GET /me/submissions под teacher.
/// then:  403 'Доступ запрещён' в обоих случаях (AC FR-021 «Роли»).
/// </summary>
public sealed class Ts151_SubmissionsRoleForbiddenTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts151_SubmissionsRoleForbiddenTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task StudentOnGridAndTeacherOnMeSubmissions_BothGet403()
    {
        var groupId = B06SubmissionsSessions.RequireGroup(_factory, "ИК-221").Id.ToString();

        // when: GET /submissions?groupId=<валидный>&semester=1&page=1 под student.
        using var studentClient = B06SubmissionsSessions.CreateStudentSessionByLogin(_factory, "student01");
        using var studentResponse = await B06SubmissionsApi.GetGridAsync(
            studentClient, groupId, semester: "1", page: "1");

        // then: 403 'Доступ запрещён'.
        Assert.Equal(HttpStatusCode.Forbidden, studentResponse.StatusCode);
        var studentRoot = await BodyAssertions.ReadRootObjectAsync(studentResponse);
        BodyAssertions.MessageIs(studentRoot, "Доступ запрещён");

        // when: GET /me/submissions?semester=1 под teacher.
        using var teacherClient = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;
        using var teacherResponse = await B06SubmissionsApi.GetMeSubmissionsAsync(teacherClient, semester: "1");

        // then: 403 'Доступ запрещён'.
        Assert.Equal(HttpStatusCode.Forbidden, teacherResponse.StatusCode);
        var teacherRoot = await BodyAssertions.ReadRootObjectAsync(teacherResponse);
        BodyAssertions.MessageIs(teacherRoot, "Доступ запрещён");
    }
}
