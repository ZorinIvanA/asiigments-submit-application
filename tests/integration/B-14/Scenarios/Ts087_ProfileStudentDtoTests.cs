using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-087 «GET /me/profile: ProfileDto с groupName» (happy_path, FR-020, P0).
///
/// given: студент в группе — группа и студент role=student (groupId → группа)
///        созданы прямым DI-сидом в IGroupRepository/IUserRepository тестового
///        хоста (методика NFR-001/FR-007: публичный API для сида не используется);
///        сессия студента — cookie access_token с JWT HS256, минтым харнесом
///        ключом Auth__JwtKey тестового хоста (ADR-022); POST /auth/login в
///        предусловиях НЕ вызывается.
/// when:  GET /api/v1/me/profile.
/// then:  200 ProfileDto {login, email, fullName, role='student',
///        groupName=текущее имя группы}. FR-020 AC «Профиль студента».
/// </summary>
public sealed class Ts087_ProfileStudentDtoTests : IClassFixture<B14WebAppFactory>
{
    private const string Login = "ts087";
    private const string Email = "ts087@x.ru";
    private const string FullName = "Студент Восемьдесят Седьмой";
    private const string GroupName = "ИК-087";

    private readonly B14WebAppFactory _factory;

    public Ts087_ProfileStudentDtoTests(B14WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileOfStudentInGroup_ReturnsProfileDtoWithCurrentGroupName()
    {
        // given: студент в группе (DI-сид); сессия студента — минтованный access-cookie.
        var group = B14Harness.SeedGroup(_factory, GroupName);
        var seeded = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: group.Id,
            password: B14Harness.TestUserPassword);
        using var client = B14Harness.CreateSessionClient(_factory, seeded.Id, UserRoles.Student);

        // when: GET /api/v1/me/profile.
        using var response = await client.GetAsync(B14Harness.ProfileEndpoint);

        // then: 200 ProfileDto с полями сида и ТЕКУЩИМ именем группы.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B14Assertions.ReadRootObjectAsync(response);
        B14Assertions.StringPropertyIs(root, "login", Login);
        B14Assertions.StringPropertyIs(root, "email", Email);
        B14Assertions.StringPropertyIs(root, "fullName", FullName);
        B14Assertions.StringPropertyIs(root, "role", UserRoles.Student);
        B14Assertions.StringPropertyIs(root, "groupName", GroupName);
    }
}
