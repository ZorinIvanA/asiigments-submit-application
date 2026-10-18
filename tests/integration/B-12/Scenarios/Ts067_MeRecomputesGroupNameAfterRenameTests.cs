using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-067 (P2, data_integrity; FR-011, FR-019) «auth/me пересчитывает имя группы
/// при переименовании».
/// given: student01 состоит в группе «ИК-221»; teacher переименовал группу
///        в «ИК-921» (PUT /groups/{id}).
/// when:  GET /auth/me под student01.
/// then:  200; groupName='ИК-921' — имя вычисляется по текущему состоянию групп
///        (FR-011: «имя группы вычисляется по текущему состоянию групп»).
/// </summary>
public sealed class Ts067_MeRecomputesGroupNameAfterRenameTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task Me_AfterGroupRename_ReturnsCurrentGroupName()
    {
        // given: student01 состоит в группе «ИК-221» (DI-сид).
        var group = B12Seed.EnsureGroup(_factory, "ИК-221");
        B12Seed.EnsureStudent(
            _factory,
            login: "student01",
            fullName: "Иванов Иван Иванович 01",
            email: "s01@x.ru",
            groupId: group.Id);

        // given: teacher переименовал группу в «ИК-921» (PUT /groups/{id}, FR-019).
        using var teacherClient = HostClients.CreateTeacherClient(_factory);
        using var rename = await teacherClient.PutAsJsonAsync(
            $"/api/v1/groups/{group.Id}",
            new { name = "ИК-921" });
        Assert.True(
            rename.IsSuccessStatusCode,
            $"given не исполнен: PUT /groups/{{id}} → {(int)rename.StatusCode} (ожидался успех по FR-019).");

        // when: GET /auth/me под student01.
        using var studentClient = HostClients.CreateStudentClient(_factory, "student01");
        using var me = await studentClient.GetAsync(B12AuthEndpoints.Me);

        // then: 200; groupName='ИК-921' — вычисляется по текущему состоянию групп.
        var root = await ApiAssert.ReadOkJsonAsync(me);
        Assert.Equal("student01", root.GetProperty("login").GetString());
        Assert.Equal("ИК-921", root.GetProperty("groupName").GetString());
    }
}
