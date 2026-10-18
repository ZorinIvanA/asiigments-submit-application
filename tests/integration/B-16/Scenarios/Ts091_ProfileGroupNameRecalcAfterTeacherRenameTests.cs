using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-091 «Профиль: groupName пересчитывается после переименования группы»
/// (happy_path, FR-015 + FR-019, P1; нумерация текущего батча B-16).
///
/// given: student01 состоит в ИК-221; активна teacher-сессия; группа ИК-221
///        переименована в 'ИК-999' запросом PUT /api/v1/groups/{id} {name:'ИК-999'},
///        ВЫПОЛНЕННЫМ ПОД СЕССИЕЙ TEACHER (FR-019/FR-022: переименование групп —
///        teacher-only; никакой иной способ переименования в кейсе не используется;
///        сессии — минтованные access-cookie, ADR-015).
/// when:  GET /api/v1/me/profile под student01.
/// then:  200, groupName='ИК-999' — имя группы вычисляется по текущему состоянию
///        групп (AC FR-015: «groupName вычисляется по текущему состоянию групп»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts091_ProfileGroupNameRecalcAfterTeacherRenameTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string OriginalGroupName = "ИК-221";
    private const string RenamedGroupName = "ИК-999";

    private readonly B16WebAppFactory _factory;

    public Ts091_ProfileGroupNameRecalcAfterTeacherRenameTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetProfile_AfterGroupRenamedViaTeacherHttpPut_ShowsCurrentGroupName()
    {
        // given: student01 состоит в ИК-221 (DI-сид группы и пользователя).
        var group = B16Harness.SeedGroup(_factory, OriginalGroupName);
        var student = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Иванов Иван Иванович 01",
            role: UserRoles.Student,
            groupId: group.Id,
            password: B16Harness.TestUserPassword);
        using var studentClient = B16Harness.CreateSessionClient(_factory, student.Id, UserRoles.Student);

        // given: активна teacher-сессия; группа ИК-221 переименована в 'ИК-999'
        // ИМЕННО запросом PUT /api/v1/groups/{id} под сессией teacher.
        using var teacherClient = B16GroupsSession.CreateTeacher(_factory);
        using var renameResponse = await B16GroupsApi.PutGroupAsync(
            teacherClient, group.Id.ToString(), RenamedGroupName);
        Assert.True(
            renameResponse.StatusCode == HttpStatusCode.OK,
            $"Шаг given «переименование группы под teacher-сессией» ожидал 200, фактически " +
            $"{(int)renameResponse.StatusCode}: {await renameResponse.Content.ReadAsStringAsync()}");

        // when: GET /me/profile под student01.
        using var response = await studentClient.GetAsync(B16Harness.ProfileEndpoint);

        // then: 200, groupName='ИК-999' — вычисляется по текущему состоянию групп.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.StringPropertyIs(root, "login", Login);
        B16Assertions.StringPropertyIs(root, "groupName", RenamedGroupName);
    }
}
