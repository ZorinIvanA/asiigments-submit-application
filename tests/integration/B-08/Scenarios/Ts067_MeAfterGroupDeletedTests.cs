using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-067 «GET /auth/me после удаления группы: groupName=null» (data_integrity,
/// FR-016 + FR-043, P0).
///
/// given: группу студента удалили — DELETE /api/v1/groups/{id} от сессии
///        преподавателя (FR-043: 204, студентам группы groupId сбрасывается).
/// when:  GET /api/v1/auth/me (сессия студента).
/// then:  200, groupName=null (висячей ссылки нет). FR-016 AC «Группа удалена».
/// </summary>
public sealed class Ts067_MeAfterGroupDeletedTests : IClassFixture<B08WebAppFactory>
{
    private const string StudentLogin = "ts067-student";
    private const string StudentEmail = "ts067-student@lab.local";
    private const string StudentFullName = "Студент ШестьдесятСемь";
    private const string TeacherLogin = "ts067-teacher";
    private const string TeacherEmail = "ts067-teacher@lab.local";
    private const string TeacherFullName = "Преподаватель ШестьдесятСемь";
    private const string GroupName = "ИК-221";

    private readonly B08WebAppFactory _factory;

    public Ts067_MeAfterGroupDeletedTests(B08WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Me_AfterGroupDeleted_ReturnsNullGroupNameWithoutDanglingLink()
    {
        // given: группа ИК-221 со студентом; сессии студента и преподавателя.
        var group = B08Host.SeedGroup(_factory, GroupName);
        using var studentClient = B08Host.CreateClient(_factory);
        var student = B08Host.SeedStudent(_factory, StudentLogin, StudentEmail, StudentFullName, groupId: group.Id);
        B08Host.EstablishSession(_factory, studentClient, student.Id, UserRoles.Student);

        using var teacherClient = B08Host.CreateClient(_factory);
        var teacher = B08Host.SeedTeacher(_factory, TeacherLogin, TeacherEmail, TeacherFullName);
        B08Host.EstablishSession(_factory, teacherClient, teacher.Id, UserRoles.Teacher);

        // when: группу студента удалили (DELETE /groups/{id}, FR-043).
        using var delete = await teacherClient.DeleteAsync($"{B08Host.GroupsEndpointPrefix}{group.Id}");

        // Предусловие кейса: удаление выполнено (204, FR-043 AC «Удаление со сбросом»).
        Assert.True(
            delete.StatusCode == HttpStatusCode.NoContent,
            $"Предусловие: DELETE /groups/{group.Id} должен вернуть 204, фактически {(int)delete.StatusCode}: {await delete.Content.ReadAsStringAsync()}");

        // when: GET /auth/me студентом.
        using var me = await studentClient.GetAsync(B08Host.MeEndpoint);

        // then: 200, groupName=null (висячей ссылки нет).
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await B08Host.ReadJsonObjectAsync(me);
        ResponseAssertions.AssertStringPropertyIs(body, "role", UserRoles.Student);
        ResponseAssertions.AssertNullProperty(body, "groupName");
    }
}
