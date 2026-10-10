using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-144 (P0, happy_path; FR-051) «PUT /students/{id}/group: исключение (groupId=null)».
/// given: Студент в группе X.
/// when: PUT /students/{id}/group {groupId:null}.
/// then: 204; groupId=null. FR-051 AC «Исключение».
/// </summary>
public sealed class Ts144_PutStudentGroupRemoveTests(B12WebAppFactory factory) : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS144_RemoveFromGroup_JsonNull_Returns204_AndGroupIdNull()
    {
        // given: студент в группе X (DI-сид); сессия teacher.
        var groupX = B12Seed.EnsureGroup(_factory, "B12-X");
        var student = B12Seed.EnsureStudent(
            _factory,
            login: "b12s144",
            fullName: "Исключаемый Исключ Исключевич",
            email: "b12s144@x.ru",
            groupId: groupX.Id);
        using var teacher = HostClients.CreateTeacherClient(_factory);

        // when: PUT /students/{id}/group {groupId:null} (литерал JSON null).
        using var put = await teacher.PutAsync(
            $"/api/v1/students/{student.Id}/group",
            B12Seed.GroupBody("null"));

        // then: 204; groupId=null (студент в выборке «без группы»).
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        using var list = await teacher.GetAsync("/api/v1/students?groupId=none");
        var root = await ApiAssert.ReadOkJsonAsync(list);
        Assert.Equal(1, root.GetProperty("total").GetInt32());
        var items = root.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(student.Id.ToString(), items[0].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("groupId").ValueKind);
    }
}
