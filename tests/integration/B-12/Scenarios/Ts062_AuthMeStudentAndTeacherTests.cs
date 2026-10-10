using System.Net.Http.Json;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-062 (P0, happy_path; FR-011) «/auth/me: студент в группе, студент без
/// группы, преподаватель; groupName пересчитывается после переименования группы».
/// given: student01 состоит в ИК-221; student31 без группы (groupId=null);
///        есть валидные сессии student01, student31 и teacher (teacher имеет
///        право на PUT /groups/{id}); сессии минтятся DI (ADR-015/ADR-022).
/// when:  GET /api/v1/auth/me с access-cookie student01; отдельно — с
///        access-cookie student31; отдельно — с access-cookie teacher; затем под
///        teacher переименование группы student01: PUT /api/v1/groups/{id}
///        {name:'ИК-901'}; повторный GET /auth/me с access-cookie student01
///        (access-токен НЕ перевыпускался).
/// then:  student01 — 200 {login:'student01', fullName:'Иванов Иван Иванович 01',
///        role:'student', groupName:'ИК-221'}; student31 — 200 {role:'student',
///        groupName:null} — ровно null (не пустая строка, не отсутствующее поле,
///        не 404); teacher — 200 {role:'teacher', groupName:null}; после
///        переименования повторный GET /auth/me student01 — 200 с
///        groupName='ИК-901' — имя вычисляется по ТЕКУЩЕМУ состоянию групп
///        (FR-011 AC «Студент в группе», «Преподаватель»; FR-011: «имя группы
///        вычисляется по текущему состоянию групп»).
/// Стимул «возвращён по замечанию ревью прошлой ревизии»: пересчёт groupName
/// после переименования (ранее покрывался плиткой Ts067_MeRecomputes…,
/// признанной дубликатом без замены).
/// </summary>
public sealed class Ts062_AuthMeStudentAndTeacherTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task Me_RolesAndGroupless_Then_RecomputesGroupNameAfterRename()
    {
        // given: student01 состоит в ИК-221; student31 без группы (groupId=null).
        var group = B12Seed.EnsureGroup(_factory, "ИК-221");
        B12Seed.EnsureStudent(
            _factory,
            login: "student01",
            fullName: "Иванов Иван Иванович 01",
            email: "s01@x.ru",
            groupId: group.Id);
        B12Seed.EnsureStudent(
            _factory,
            login: "student31",
            fullName: "Сидоров Сидор Сидорович 31",
            email: "s31@x.ru");

        // given: валидные сессии student01, student31 и teacher; access student01
        // минтится ОДИН раз — повторный GET после переименования идёт с тем же токеном.
        using var student01 = HostClients.CreateStudentClient(_factory, "student01");
        using var student31 = HostClients.CreateStudentClient(_factory, "student31");
        using var teacher = HostClients.CreateTeacherClient(_factory);

        // when/then: GET /auth/me с access-cookie student01 — дословный MeDto кейса.
        using var meStudent01 = await student01.GetAsync(B12AuthEndpoints.Me);
        var rootStudent01 = await ApiAssert.ReadOkJsonAsync(meStudent01);
        ApiAssert.HasExactlyProperties(rootStudent01, "login", "fullName", "role", "groupName");
        Assert.Equal("student01", rootStudent01.GetProperty("login").GetString());
        Assert.Equal("Иванов Иван Иванович 01", rootStudent01.GetProperty("fullName").GetString());
        Assert.Equal(UserRoles.Student, rootStudent01.GetProperty("role").GetString());
        Assert.Equal("ИК-221", rootStudent01.GetProperty("groupName").GetString());

        // when/then: GET /auth/me student31 — 200, groupName РОВНО null (свойство
        // присутствует со значением null: не пустая строка, не отсутствие, не 404).
        using var meStudent31 = await student31.GetAsync(B12AuthEndpoints.Me);
        var rootStudent31 = await ApiAssert.ReadOkJsonAsync(meStudent31);
        Assert.Equal("student31", rootStudent31.GetProperty("login").GetString());
        Assert.Equal(UserRoles.Student, rootStudent31.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, rootStudent31.GetProperty("groupName").ValueKind);

        // when/then: GET /auth/me teacher — 200 {role:'teacher', groupName:null}.
        using var meTeacher = await teacher.GetAsync(B12AuthEndpoints.Me);
        var rootTeacher = await ApiAssert.ReadOkJsonAsync(meTeacher);
        Assert.Equal(UserRoles.Teacher, rootTeacher.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, rootTeacher.GetProperty("groupName").ValueKind);

        // when: под teacher переименование группы student01: PUT /groups/{id}
        // {name:'ИК-901'} (FR-019: успех — 200).
        using var rename = await teacher.PutAsJsonAsync(
            $"/api/v1/groups/{group.Id}",
            new { name = "ИК-901" });
        Assert.True(
            rename.StatusCode == HttpStatusCode.OK,
            $"Шаг when неисполним: PUT /api/v1/groups/{{id}} → {(int)rename.StatusCode} (ожидался 200 по FR-019).");

        // when/then: повторный GET /auth/me с НЕперевыпущенным access-cookie
        // student01 — groupName='ИК-901': имя вычисляется по текущему состоянию
        // групп на момент запроса, а не снапшотом на момент выпуска токена.
        using var meAfterRename = await student01.GetAsync(B12AuthEndpoints.Me);
        var rootAfterRename = await ApiAssert.ReadOkJsonAsync(meAfterRename);
        Assert.Equal("student01", rootAfterRename.GetProperty("login").GetString());
        Assert.Equal(UserRoles.Student, rootAfterRename.GetProperty("role").GetString());
        Assert.Equal("ИК-901", rootAfterRename.GetProperty("groupName").GetString());
    }
}
