using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-167 «Сдачи: обмен ролями — student на ведомости и teacher на своих
/// сдачах» (негативный, P0, FR-021/FR-022).
///
/// given: валидные сессии student01 и teacher (демо-набор сида).
/// when:  GET /api/v1/submissions?groupId=&lt;ИК-221&gt;&amp;semester=1 под
///        student01; GET /api/v1/me/submissions?semester=1 под teacher.
/// then:  403 'Доступ запрещён' в обоих случаях (FR-021 AC «Роли»).
/// </summary>
public sealed class Ts167_SubmissionsRoleSwapTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts167_SubmissionsRoleSwapTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task StudentOnGridAndTeacherOnMeSubmissions_BothGet403()
    {
        // given: валидные сессии student01 и teacher.
        var groupId = B06SubmissionsSessions.RequireGroup(_factory, "ИК-221").Id.ToString();
        using var studentClient = B06SubmissionsSessions.CreateStudentSessionByLogin(_factory, "student01");
        using var teacherClient = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when: GET /submissions?groupId=<ИК-221>&semester=1 под student01.
        using var studentResponse = await B06SubmissionsApi.GetGridAsync(
            studentClient, groupId, semester: "1", page: "1");

        // then: 403 'Доступ запрещён'.
        Assert.Equal(HttpStatusCode.Forbidden, studentResponse.StatusCode);
        var studentRoot = await BodyAssertions.ReadRootObjectAsync(studentResponse);
        BodyAssertions.MessageIs(studentRoot, "Доступ запрещён");

        // when: GET /me/submissions?semester=1 под teacher.
        using var teacherResponse = await B06SubmissionsApi.GetMeSubmissionsAsync(teacherClient, semester: "1");

        // then: 403 'Доступ запрещён'.
        Assert.Equal(HttpStatusCode.Forbidden, teacherResponse.StatusCode);
        var teacherRoot = await BodyAssertions.ReadRootObjectAsync(teacherResponse);
        BodyAssertions.MessageIs(teacherRoot, "Доступ запрещён");
    }
}
