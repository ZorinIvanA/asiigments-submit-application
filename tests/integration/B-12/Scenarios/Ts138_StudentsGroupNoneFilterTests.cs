using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-138 (P0, happy_path; FR-050) «GET /students?groupId=none: только студенты без группы».
/// given: Студенты с группой и без; сессия teacher.
/// when: GET /students?groupId=none.
/// then: 200; только студенты с groupId=null. FR-050 AC «Фильтр без группы».
/// </summary>
public sealed class Ts138_StudentsGroupNoneFilterTests(B12WebAppFactory factory) : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS138_GroupIdNone_ReturnsOnlyStudentsWithoutGroup()
    {
        // given: студент с группой и студент без группы; сессия teacher.
        var group = B12Seed.EnsureGroup(_factory, "B12-138-ИК");
        B12Seed.EnsureStudent(
            _factory,
            login: "b12s138with",
            fullName: "С Группой С Групович",
            email: "b12s138with@x.ru",
            groupId: group.Id);
        var withoutGroup = B12Seed.EnsureStudent(
            _factory,
            login: "b12s138none",
            fullName: "Без Группы Безгрупович",
            email: "b12s138none@x.ru");
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /students?groupId=none.
        using var response = await client.GetAsync("/api/v1/students?groupId=none");

        // then: 200; только студенты с groupId=null.
        var root = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(1, root.GetProperty("total").GetInt32());
        var items = root.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(withoutGroup.Id.ToString(), items[0].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("groupId").ValueKind);
    }
}
