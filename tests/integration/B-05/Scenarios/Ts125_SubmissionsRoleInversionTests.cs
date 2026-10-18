using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-125 «Ведомости: инверсия ролей — 403» (negative, FR-021/FR-022).
///
/// given: валидные сессии student и teacher (минт, ADR-015).
/// when:  GET /api/v1/submissions (с валидными параметрами) под student;
///        GET /api/v1/me/submissions?semester=1 под teacher.
/// then:  403 'Доступ запрещён' в обоих случаях (FR-021 AC «Роли»).
/// </summary>
public sealed class Ts125_SubmissionsRoleInversionTests : IClassFixture<B05WebAppFactory>
{
    private const string GridEndpoint = "/api/v1/submissions";
    private const string MySubmissionsEndpoint = "/api/v1/me/submissions";

    private readonly B05WebAppFactory _factory;

    public Ts125_SubmissionsRoleInversionTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GridUnderStudent_AndMySubmissionsUnderTeacher_AreBoth403()
    {
        // given: валидные сессии student (student01) и teacher.
        var student01 = B05SeedLookup.StudentByLogin(_factory, "student01");
        using var studentClient = B05MintedSessions.Create(_factory);
        B05MintedSessions.MintAccessCookie(_factory, studentClient, student01.Id, UserRoles.Student);
        using var teacherClient = B05MintedSessions.CreateTeacherClient(_factory);

        // when: GET /submissions с валидными параметрами под student.
        var ik221 = B05SeedLookup.GroupByName(_factory, "ИК-221");
        using var gridAsStudent = await studentClient.GetAsync(
            $"{GridEndpoint}?groupId={ik221.Id}&semester=1&page=1");

        // when: GET /me/submissions?semester=1 под teacher.
        using var myAsTeacher = await teacherClient.GetAsync($"{MySubmissionsEndpoint}?semester=1");

        // then: 403 'Доступ запрещён' в обоих случаях.
        foreach (var response in new[] { gridAsStudent, myAsTeacher })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var root = await BodyAssertions.ReadRootObjectAsync(response);
            BodyAssertions.MessageIs(root, "Доступ запрещён");
        }
    }
}
