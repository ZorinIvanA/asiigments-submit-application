using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-079 «me/profile GET: полный ProfileDto с groupName (в группе и без
/// группы)» (happy_path, FR-015, P0).
///
/// given: student01 авторизован и состоит в ИК-221; student31 авторизован и не
///        состоит ни в одной группе — группа и пользователи созданы прямым
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
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts079_ProfileGetStudentWithGroupTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Иванов Иван Иванович 01";
    private const string GroupName = "ИК-221";

    private const string NoGroupLogin = "student31";
    private const string NoGroupEmail = "student31@example.com";
    private const string NoGroupFullName = "Иванов Иван Иванович 31";

    private readonly B16WebAppFactory _factory;

    public Ts079_ProfileGetStudentWithGroupTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetProfile_StudentInGroup_ReturnsFullDtoWithGroupName()
    {
        // given: student01 состоит в ИК-221 (DI-сид группы и пользователя), сессия минтована.
        var group = B16Harness.SeedGroup(_factory, GroupName);
        var student = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: group.Id,
            password: B16Harness.TestUserPassword);
        using var client = B16Harness.CreateSessionClient(_factory, student.Id, UserRoles.Student);

        // when: GET /api/v1/me/profile с access-cookie student01.
        using var response = await client.GetAsync(B16Harness.ProfileEndpoint);

        // then: 200 и ProfileDto с дословно кейсовыми значениями.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.StringPropertyIs(root, "login", Login);
        B16Assertions.StringPropertyIs(root, "email", Email);
        B16Assertions.StringPropertyIs(root, "fullName", FullName);
        B16Assertions.StringPropertyIs(root, "role", UserRoles.Student);
        B16Assertions.StringPropertyIs(root, "groupName", GroupName);
    }

    [Fact]
    public async Task GetProfile_StudentWithoutGroup_ReturnsDtoWithExactlyNullGroupName()
    {
        // given: student31 не состоит ни в одной группе (GroupId=null), сессия минтована.
        var student = B16Harness.SeedUser(
            _factory,
            login: NoGroupLogin,
            email: NoGroupEmail,
            fullName: NoGroupFullName,
            role: UserRoles.Student,
            groupId: null,
            password: B16Harness.TestUserPassword);
        using var client = B16Harness.CreateSessionClient(_factory, student.Id, UserRoles.Student);

        // when: GET /api/v1/me/profile с access-cookie student31.
        using var response = await client.GetAsync(B16Harness.ProfileEndpoint);

        // then: 200 (не 404) и groupName — ровно JSON-null: поле присутствует,
        // но его значение — не строка (в частности, не пустая строка).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.StringPropertyIs(root, "login", NoGroupLogin);
        B16Assertions.StringPropertyIs(root, "email", NoGroupEmail);
        B16Assertions.StringPropertyIs(root, "fullName", NoGroupFullName);
        B16Assertions.StringPropertyIs(root, "role", UserRoles.Student);
        B16Assertions.PropertyIsNull(root, "groupName");
    }
}
