using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-062 (P0, happy_path; FR-011) «/auth/me: студент в группе, студент без
/// группы, преподаватель».
/// given: student01 состоит в ИК-221 (DI-сид B11AuthSessions.SeedStudent, ADR-010:
///        демо-набор отключён); student31 без группы (groupId=null); валидные
///        сессии student01, student31 и teacher (минт access-JWT, ADR-015).
/// when:  GET /api/v1/auth/me с access-cookie student01; отдельно — с access-cookie
///        student31; отдельно — с access-cookie teacher.
/// then:  student01 — 200 {login:'student01', fullName:'Иванов Иван Иванович 01',
///        role:'student', groupName:'ИК-221'}; student31 — 200 {role:'student',
///        groupName:null} — ровно null (не пустая строка, не отсутствующее поле,
///        не 404); teacher — 200 {role:'teacher', groupName:null} (FR-011 AC
///        «Студент в группе», «Преподаватель»; groupName=null — FR-011).
/// </summary>
public sealed class Ts062_MeStudentGrouplessTeacherTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS062_Me_StudentInGroup_ReturnsGroupNameOfCurrentState()
    {
        // given: student01 состоит в ИК-221; валидная access-сессия студента.
        B11AuthSessions.SeedStudent(
            _factory, "student01", "Иванов Иван Иванович 01", groupName: "ИК-221");
        using var client = B11AuthSessions.CreateSessionClient(_factory, "student01");

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
    public async Task TS062_Me_StudentWithoutGroup_GroupNameIsExactlyNull()
    {
        // given: student31 без группы (groupId=null); валидная access-сессия.
        B11AuthSessions.SeedStudent(_factory, "student31", "Сидоров Сидор Сидорович");
        using var client = B11AuthSessions.CreateSessionClient(_factory, "student31");

        // when: GET /api/v1/auth/me с access-cookie student31.
        using var response = await client.GetAsync("/api/v1/auth/me");

        // then: 200 {role:'student', groupName:null} — groupName ровно null
        // (не пустая строка, не отсутствующее поле, не 404).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await ApiAssert.ReadJsonAsync(response);
        ApiAssert.HasExactlyProperties(root, "login", "fullName", "role", "groupName");
        Assert.Equal(UserRoles.Student, root.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("groupName").ValueKind);
    }

    [Fact]
    public async Task TS062_Me_Teacher_GroupNameIsNull()
    {
        // given: валидная сессия сид-преподавателя (учётка Seed__TeacherLogin).
        using var client = B11AuthSessions.CreateSessionClient(
            _factory, SeedOptions.DefaultTeacherLogin);

        // when: GET /api/v1/auth/me с access-cookie teacher.
        using var response = await client.GetAsync("/api/v1/auth/me");

        // then: 200 {role:'teacher', groupName:null} — форма MeDto та же.
        var root = await ApiAssert.ReadOkJsonAsync(response);
        ApiAssert.HasExactlyProperties(root, "login", "fullName", "role", "groupName");
        Assert.Equal(SeedOptions.DefaultTeacherLogin, root.GetProperty("login").GetString());
        Assert.Equal(UserRoles.Teacher, root.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("groupName").ValueKind);
    }
}
