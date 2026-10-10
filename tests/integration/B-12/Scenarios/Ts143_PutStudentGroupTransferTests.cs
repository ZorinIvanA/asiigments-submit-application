using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-143 (P1, happy_path; FR-051) «PUT /students/{id}/group: перевод между группами».
/// given: Студент в группе X; группа Y существует.
/// when: PUT /students/{id}/group {groupId:Y}.
/// then: 204; студент в Y (groupId=Y). FR-051 AC «Перевод».
/// </summary>
public sealed class Ts143_PutStudentGroupTransferTests(B12WebAppFactory factory) : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS143_TransferBetweenGroups_Returns204_AndStudentInY()
    {
        // given: студент в группе X (DI-сид); группа Y существует; сессия teacher.
        var groupX = B12Seed.EnsureGroup(_factory, "B12-X");
        var groupY = B12Seed.EnsureGroup(_factory, "B12-Y");
        var student = B12Seed.EnsureStudent(
            _factory,
            login: "b12s143",
            fullName: "Переводимый Перевод Переводович",
            email: "b12s143@x.ru",
            groupId: groupX.Id);
        using var teacher = HostClients.CreateTeacherClient(_factory);

        // when: PUT /students/{id}/group {groupId:Y}.
        using var put = await teacher.PutAsync(
            $"/api/v1/students/{student.Id}/group",
            B12Seed.GroupBody($"\"{groupY.Id}\""));

        // then: 204; студент в Y (groupId=Y).
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        using var list = await teacher.GetAsync($"/api/v1/students?groupId={groupY.Id}");
        var root = await ApiAssert.ReadOkJsonAsync(list);
        Assert.Equal(1, root.GetProperty("total").GetInt32());
        var items = root.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(student.Id.ToString(), items[0].GetProperty("id").GetString());
        Assert.Equal(groupY.Id.ToString(), items[0].GetProperty("groupId").GetString());
    }
}
