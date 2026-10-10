using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-155 «Сдачи: роли — student на ведомости и teacher на своих сдачах — 403»
/// (негативный, P0, FR-021/FR-022).
///
/// given: существуют валидные сессии student и teacher (демо-сид).
/// when:  GET /api/v1/submissions?groupId=&lt;валидный&gt;&amp;semester=1 под student;
///        GET /api/v1/me/submissions?semester=1 под teacher.
/// then:  403 'Доступ запрещён' в обоих случаях (FR-021 AC «Роли»).
/// </summary>
public sealed class Ts155_SubmissionsRoleMatrixTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts155_SubmissionsRoleMatrixTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task StudentOnGridAndTeacherOnMeSubmissions_BothGet403()
    {
        var groupId = B06SubmissionsSessions.RequireGroup(_factory, "ИК-221").Id.ToString();

        // when: GET /submissions под student.
        using var studentClient = B06SubmissionsSessions.CreateStudentSessionByLogin(_factory, "student01");
        using var studentResponse = await B06SubmissionsApi.GetGridAsync(
            studentClient, groupId, semester: "1", page: "1");

        // then: 403 'Доступ запрещён'.
        Assert.Equal(HttpStatusCode.Forbidden, studentResponse.StatusCode);
        var studentRoot = await BodyAssertions.ReadRootObjectAsync(studentResponse);
        BodyAssertions.MessageIs(studentRoot, "Доступ запрещён");

        // when: GET /me/submissions под teacher.
        using var teacherClient = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;
        using var teacherResponse = await B06SubmissionsApi.GetMeSubmissionsAsync(teacherClient, semester: "1");

        // then: 403 'Доступ запрещён'.
        Assert.Equal(HttpStatusCode.Forbidden, teacherResponse.StatusCode);
        var teacherRoot = await BodyAssertions.ReadRootObjectAsync(teacherResponse);
        BodyAssertions.MessageIs(teacherRoot, "Доступ запрещён");
    }
}
