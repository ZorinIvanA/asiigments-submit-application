using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-079 «me/profile GET: полный ProfileDto с groupName (в группе и без
/// группы)» (happy_path, FR-015, P0).
///
/// given: student01 авторизован и состоит в ИК-221; student31 авторизован и
///        не состоит ни в одной группе — группа и пользователи созданы прямым
///        DI-сидом с дословно кейсовыми значениями (email studentNN@example.com,
///        ФИО «Иванов Иван Иванович NN»); сессии — минтованные access-cookie
///        (ADR-015).
/// when:  GET /api/v1/me/profile с access-cookie student01; отдельно — с
///        access-cookie student31.
/// then:  student01 — 200 {login:'student01', email:'student01@example.com',
///        fullName:'Иванов Иван Иванович 01', role:'student',
///        groupName:'ИК-221'}; student31 — 200 {…, groupName:null} — ровно null
///        при отсутствии группы (не пустая строка, не отсутствующее поле, не
///        404) (FR-015 AC «GET профиля студента с группой»; FR-015:
///        «groupName вычисляется по текущему состоянию групп»).
/// </summary>
public sealed class Ts079_ProfileGetStudentWithGroupTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Иванов Иван Иванович 01";
    private const string GroupName = "ИК-221";

    private const string NoGroupLogin = "student31";
    private const string NoGroupEmail = "student31@example.com";
    private const string NoGroupFullName = "Иванов Иван Иванович 31";

    private readonly B17WebAppFactory _factory;

    public Ts079_ProfileGetStudentWithGroupTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetProfile_StudentInGroup_ReturnsFullDtoWithGroupName()
    {
        // given: student01 состоит в ИК-221 (DI-сид группы и пользователя), сессия минтована.
        var group = B17ProfileHost.SeedGroup(_factory, GroupName);
        var student = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: group.Id,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, student.Id, UserRoles.Student);

        // when: GET /api/v1/me/profile с access-cookie student01.
        using var response = await client.GetAsync(B17ProfileHost.ProfileEndpoint);

        // then: 200 и ProfileDto с дословно кейсовыми значениями.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.StringPropertyIs(root, "login", Login);
        B17BodyAssertions.StringPropertyIs(root, "email", Email);
        B17BodyAssertions.StringPropertyIs(root, "fullName", FullName);
        B17BodyAssertions.StringPropertyIs(root, "role", UserRoles.Student);
        B17BodyAssertions.StringPropertyIs(root, "groupName", GroupName);
    }

    [Fact]
    public async Task GetProfile_StudentWithoutGroup_ReturnsDtoWithExactlyNullGroupName()
    {
        // given: student31 не состоит ни в одной группе (GroupId=null), сессия минтована.
        var student = B17ProfileHost.SeedUser(
            _factory,
            login: NoGroupLogin,
            email: NoGroupEmail,
            fullName: NoGroupFullName,
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, student.Id, UserRoles.Student);

        // when: GET /api/v1/me/profile с access-cookie student31.
        using var response = await client.GetAsync(B17ProfileHost.ProfileEndpoint);

        // then: 200 (не 404) и groupName — ровно JSON-null: поле присутствует,
        // но его значение — не строка (в частности, не пустая строка).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.StringPropertyIs(root, "login", NoGroupLogin);
        B17BodyAssertions.StringPropertyIs(root, "email", NoGroupEmail);
        B17BodyAssertions.StringPropertyIs(root, "fullName", NoGroupFullName);
        B17BodyAssertions.StringPropertyIs(root, "role", UserRoles.Student);
        B17BodyAssertions.PropertyIsNull(root, "groupName");
    }
}
