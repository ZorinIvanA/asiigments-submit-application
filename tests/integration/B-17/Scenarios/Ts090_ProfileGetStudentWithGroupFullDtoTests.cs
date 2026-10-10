using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-090 «Профиль: GET студента с группой — полный ProfileDto» (happy_path,
/// FR-015, P0).
///
/// given: student01 авторизован и состоит в ИК-221 (демо-сид — пользователи и
///        группа созданы прямым DI-сидом с дословно кейсовыми значениями
///        email studentNN@example.com, ФИО «Иванов Иван Иванович NN»; сессия —
///        минтованный access-cookie, ADR-015).
/// when:  GET /api/v1/me/profile с access-cookie student01.
/// then:  200 {login:'student01', email:'student01@example.com',
///        fullName:'Иванов Иван Иванович 01', role:'student',
///        groupName:'ИК-221'} (FR-015 AC «GET профиля студента с группой»).
/// </summary>
public sealed class Ts090_ProfileGetStudentWithGroupFullDtoTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Иванов Иван Иванович 01";
    private const string GroupName = "ИК-221";

    private readonly B17WebAppFactory _factory;

    public Ts090_ProfileGetStudentWithGroupFullDtoTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetProfile_StudentInGroup_ReturnsFullProfileDto()
    {
        // given: student01 состоит в ИК-221 (демо-сид группы и пользователя), сессия минтована.
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

        // when: GET /api/v1/me/profile.
        using var response = await client.GetAsync(B17ProfileHost.ProfileEndpoint);

        // then: 200 и полный ProfileDto с дословно кейсовыми значениями.
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
}
