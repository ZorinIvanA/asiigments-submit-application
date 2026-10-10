using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-062 (P0, happy_path; FR-011) «/auth/me: студент в группе и преподаватель».
/// given: student01 состоит в ИК-221 (DI-сид B10AuthSessions.SeedStudentWithGroup,
///        ADR-010: демо-набор отключён); есть валидные сессии student01 (минт,
///        ADR-022) и teacher (сид-учётка Seed__*, HostClients).
/// when:  GET /api/v1/auth/me с access-cookie student01; отдельно — с access-cookie
///        teacher.
/// then:  student01 — 200 MeDto {login:'student01', fullName:'Иванов Иван Иванович
///        01', role:'student', groupName:'ИК-221'}; teacher — 200 {role:'teacher',
///        groupName:null} (FR-011 AC «Студент в группе», «Преподаватель»);
///        groupName вычисляется по текущему состоянию групп, форма MeDto —
///        ровно {login, fullName, role, groupName}.
/// </summary>
public sealed class Ts062_AuthMeStudentAndTeacherTests(B10NoDemoWebAppFactory factory)
    : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS062_GetMe_AsStudentInGroup_ReturnsGroupAndRole()
    {
        // given: student01 в группе ИК-221; валидная access-сессия студента.
        B10AuthSessions.SeedStudentWithGroup(
            _factory,
            login: "student01",
            fullName: "Иванов Иван Иванович 01",
            groupName: "ИК-221");
        using var client = HostClients.CreateStudentClient(_factory, "student01");

        // when: GET /api/v1/auth/me с access-cookie student01.
        using var response = await client.GetAsync("/api/v1/auth/me");

        // then: 200 MeDto с дословными значениями кейса.
        var root = await ApiAssert.ReadOkJsonAsync(response);
        ApiAssert.HasExactlyProperties(root, "login", "fullName", "role", "groupName");
        Assert.Equal("student01", root.GetProperty("login").GetString());
        Assert.Equal("Иванов Иван Иванович 01", root.GetProperty("fullName").GetString());
        Assert.Equal(UserRoles.Student, root.GetProperty("role").GetString());
        Assert.Equal("ИК-221", root.GetProperty("groupName").GetString());
    }

    [Fact]
    public async Task TS062_GetMe_AsTeacher_ReturnsTeacherWithoutGroup()
    {
        // given: валидная сессия сид-преподавателя (учётка создаётся сидом из Seed__*).
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /api/v1/auth/me с access-cookie teacher.
        using var response = await client.GetAsync("/api/v1/auth/me");

        // then: 200 {role:'teacher', groupName:null}; форма MeDto — та же.
        var root = await ApiAssert.ReadOkJsonAsync(response);
        ApiAssert.HasExactlyProperties(root, "login", "fullName", "role", "groupName");
        Assert.Equal(UserRoles.Teacher, root.GetProperty("role").GetString());
        Assert.Equal(SeedOptions.DefaultTeacherLogin, root.GetProperty("login").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("groupName").ValueKind);
    }
}
