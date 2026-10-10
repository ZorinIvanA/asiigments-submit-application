using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-079 «me/profile GET: полный ProfileDto с groupName» (happy_path, FR-015, P0).
///
/// given: student01 авторизован и состоит в ИК-221 — группа и студент созданы
///        прямым DI-сидом в IGroupRepository/IUserRepository тестового хоста
///        (методика зоны, ADR-015/CR-001); сессия — cookie access_token с JWT
///        HS256, минтым харнесом ключом Auth__JwtKey тестового хоста.
/// when:  GET /api/v1/me/profile с его access-cookie.
/// then:  200 {login:'student01', email:'student01@example.com',
///        fullName:'Иванов Иван Иванович 01', role:'student', groupName:'ИК-221'}
///        — FR-015 AC «GET профиля студента с группой».
/// </summary>
public sealed class Ts079_MeProfileGetWithGroupTests : IClassFixture<B15WebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Иванов Иван Иванович 01";
    private const string GroupName = "ИК-221";

    private readonly B15WebAppFactory _factory;

    public Ts079_MeProfileGetWithGroupTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetProfile_StudentInGroup_ReturnsFullProfileDto()
    {
        // given: student01 состоит в ИК-221 (DI-сид группы и студента).
        var group = B15Harness.SeedGroup(_factory, GroupName);
        var user = B15Harness.SeedStudent(_factory, fullName: FullName, login: Login, email: Email, groupId: group.Id);
        using var client = B15Harness.CreateSessionClient(_factory, user.Id);

        // when: GET профиля с access-cookie студента.
        using var response = await client.GetAsync(B15Harness.ProfileEndpoint);

        // then: 200 и полный ProfileDto с groupName по текущему состоянию групп.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.StringPropertyIs(body, "login", Login);
        BodyAssertions.StringPropertyIs(body, "email", Email);
        BodyAssertions.StringPropertyIs(body, "fullName", FullName);
        BodyAssertions.StringPropertyIs(body, "role", "student");
        BodyAssertions.StringPropertyIs(body, "groupName", GroupName);
    }
}
