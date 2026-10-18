using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-149 (P1, boundary; FR-060) «GET /submissions: page=0 нормализуется в 1».
/// given: Группа со студентами; semester=1.
/// when: GET /api/v1/submissions?groupId=X&semester=1&page=0.
/// then: page=1, первая страница. FR-060 AC «Некорректная страница».
/// </summary>
public sealed class Ts149_SubmissionsPageZeroNormalizedTests(B13WebAppFactory factory) : IClassFixture<B13WebAppFactory>
{
    private readonly B13WebAppFactory _factory = factory;

    [Fact]
    public async Task TS149_GridPageZero_IsNormalizedToFirstPage()
    {
        // given: группа с тремя студентами (известный порядок сортировки); semester=1.
        var group = B13Seed.AddGroup(_factory, "Группа TS-149");
        var first = B13Seed.AddStudent(_factory, "ts149-s1", "Алексеев", group.Id);
        var second = B13Seed.AddStudent(_factory, "ts149-s2", "Борисов", group.Id);
        var third = B13Seed.AddStudent(_factory, "ts149-s3", "Васильев", group.Id);
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /submissions?groupId=X&semester=1&page=0.
        using var response = await client.GetAsync(
            $"/api/v1/submissions?groupId={Uri.EscapeDataString(group.Id.ToString())}&semester=1&page=0");

        // then: 200; page=1, первая страница — все три студента по сортировке; total=3.
        var body = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(3, body.GetProperty("total").GetInt32());
        var students = body.GetProperty("students");
        Assert.Equal(3, students.GetArrayLength());
        Assert.Equal(first.Id.ToString(), students[0].GetProperty("id").GetString());
        Assert.Equal("Алексеев", students[0].GetProperty("fullName").GetString());
        Assert.Equal(second.Id.ToString(), students[1].GetProperty("id").GetString());
        Assert.Equal("Борисов", students[1].GetProperty("fullName").GetString());
        Assert.Equal(third.Id.ToString(), students[2].GetProperty("id").GetString());
        Assert.Equal("Васильев", students[2].GetProperty("fullName").GetString());
    }
}
