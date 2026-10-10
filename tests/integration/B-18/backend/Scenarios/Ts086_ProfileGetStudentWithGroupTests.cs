using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-086 «Профиль: GET студента с группой» (happy_path, FR-015, P0).
///
/// given: student01 авторизован и состоит в ИК-221 — пользователь создан прямым
///        DI-сидом с дословно кейсовыми значениями (email student01@example.com,
///        ФИО «Иванов Иван Иванович 01», группа ИК-221); сессия — минтованный
///        access-cookie (ADR-015).
/// when:  GET /api/v1/me/profile под student01.
/// then:  200; ProfileDto {login:'student01', email:'student01@example.com',
///        fullName:'Иванов Иван Иванович 01', role:'student', groupName:'ИК-221'}
///        (FR-015 AC «GET профиля студента с группой»; IF-013: groupName
///        вычисляется по текущему состоянию групп).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts086_ProfileGetStudentWithGroupTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Иванов Иван Иванович 01";
    private const string GroupName = "ИК-221";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts086_ProfileGetStudentWithGroupTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetProfile_ReturnsStudentDtoWithGroupName()
    {
        // given: student01 состоит в ИК-221 (DI-сид группы и пользователя), сессия минтована.
        var group = B18ProfileHarness.SeedGroup(_factory, GroupName);
        var student = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: group.Id,
            password: B18ProfileHarness.TestUserPassword);
        using var client = B18ProfileHarness.CreateSessionClient(_factory, student.Id, UserRoles.Student);

        // when: GET /api/v1/me/profile под student01.
        using var response = await client.GetAsync(B18ProfileHarness.ProfileEndpoint);

        // then: 200 и ProfileDto с дословно кейсовыми значениями.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B18ProfileAssertions.ReadRootObjectAsync(response);
        B18ProfileAssertions.StringPropertyIs(root, "login", Login);
        B18ProfileAssertions.StringPropertyIs(root, "email", Email);
        B18ProfileAssertions.StringPropertyIs(root, "fullName", FullName);
        B18ProfileAssertions.StringPropertyIs(root, "role", UserRoles.Student);
        B18ProfileAssertions.StringPropertyIs(root, "groupName", GroupName);
    }
}
