using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-063 (P0, happy_path; FR-011) «auth/me: студент в группе».
/// given: Сид развёрнут; student01 состоит в ИК-221; валидный access-cookie student01.
/// when:  GET /api/v1/auth/me.
/// then:  200; MeDto {login:'student01', fullName:'Иванов Иван Иванович 01',
///        role:'student', groupName:'ИК-221'} (FR-011 AC «Студент в группе»).
/// </summary>
public sealed class Ts063_MeStudentInGroupTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task Me_ForStudentInGroup_ReturnsExactMeDto()
    {
        // given: student01 состоит в ИК-221 (DI-сид); валидный access-cookie student01.
        var group = B12Seed.EnsureGroup(_factory, "ИК-221");
        B12Seed.EnsureStudent(
            _factory,
            login: "student01",
            fullName: "Иванов Иван Иванович 01",
            email: "s01@x.ru",
            groupId: group.Id);
        using var client = HostClients.CreateStudentClient(_factory, "student01");

        // when: GET /auth/me.
        using var response = await client.GetAsync(B12AuthEndpoints.Me);

        // then: 200; MeDto с точными полями кейса.
        var root = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal("student01", root.GetProperty("login").GetString());
        Assert.Equal("Иванов Иван Иванович 01", root.GetProperty("fullName").GetString());
        Assert.Equal("student", root.GetProperty("role").GetString());
        Assert.Equal("ИК-221", root.GetProperty("groupName").GetString());
    }
}
