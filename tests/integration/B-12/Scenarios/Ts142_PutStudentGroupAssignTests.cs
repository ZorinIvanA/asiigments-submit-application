using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-142 (P0, happy_path; FR-051) «PUT /students/{id}/group: включение в группу».
/// given: Студент без группы; группа X существует; сессия teacher.
/// when: PUT /api/v1/students/{id}/group {groupId:X}; GET /students?groupId=X;
///       GET /me/submissions (сессия студента).
/// then: 204; студент с groupId=X и groupName=имя X; GET /me/submissions студента →
/// hasGroup=true. FR-051 AC «Включение».
/// </summary>
public sealed class Ts142_PutStudentGroupAssignTests(B12WebAppFactory factory) : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS142_AssignToGroup_Returns204_AndGroupVisible()
    {
        // given: студент без группы; группа X существует; сессия teacher.
        var group = B12Seed.EnsureGroup(_factory, "B12-X");
        var student = B12Seed.EnsureStudent(
            _factory,
            login: "b12s142",
            fullName: "Включаемый Включ Включевич",
            email: "b12s142@x.ru");
        using var teacher = HostClients.CreateTeacherClient(_factory);

        // when: PUT /students/{id}/group {groupId:X}.
        using var put = await teacher.PutAsync(
            $"/api/v1/students/{student.Id}/group",
            B12Seed.GroupBody($"\"{group.Id}\""));

        // then: 204.
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        // then: GET /students?groupId=X — студент с groupId=X и groupName=имя X.
        using var list = await teacher.GetAsync($"/api/v1/students?groupId={group.Id}");
        var root = await ApiAssert.ReadOkJsonAsync(list);
        Assert.Equal(1, root.GetProperty("total").GetInt32());
        var items = root.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(student.Id.ToString(), items[0].GetProperty("id").GetString());
        Assert.Equal(group.Id.ToString(), items[0].GetProperty("groupId").GetString());
        Assert.Equal(group.Name, items[0].GetProperty("groupName").GetString());

        // then: GET /me/submissions (сессия студента) → hasGroup=true.
        using var studentClient = HostClients.CreateStudentClient(_factory, "b12s142");
        using var my = await studentClient.GetAsync("/api/v1/me/submissions?semester=1");
        var myRoot = await ApiAssert.ReadOkJsonAsync(my);
        Assert.True(myRoot.GetProperty("hasGroup").GetBoolean());
    }
}
