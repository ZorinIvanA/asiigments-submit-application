using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-066 «GET /auth/me: студент в группе получает groupName» (happy_path,
/// FR-016, P0).
///
/// given: студент включён в группу ИК-221 (DI-сид: IGroupRepository.Add +
///        User.GroupId — кейс явно разрешает DI-сид); сессия студента (минт,
///        ADR-022).
/// when:  GET /api/v1/auth/me.
/// then:  200 {role:'student', groupName:'ИК-221', login, fullName}.
///        FR-016 AC «Студент в группе».
/// </summary>
public sealed class Ts066_MeStudentInGroupTests : IClassFixture<B08WebAppFactory>
{
    private const string Login = "ts066-student";
    private const string Email = "ts066@lab.local";
    private const string FullName = "Студент ШестьдесятШесть";
    private const string GroupName = "ИК-221";

    private readonly B08WebAppFactory _factory;

    public Ts066_MeStudentInGroupTests(B08WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Me_StudentInGroup_ReturnsGroupNameByCurrentState()
    {
        // given: студент включён в группу ИК-221 (DI-сид); сессия студента.
        var group = B08Host.SeedGroup(_factory, GroupName);
        using var client = B08Host.CreateClient(_factory);
        var user = B08Host.SeedStudent(_factory, Login, Email, FullName, groupId: group.Id);
        B08Host.EstablishSession(_factory, client, user.Id, UserRoles.Student);

        // when: GET /api/v1/auth/me.
        using var me = await client.GetAsync(B08Host.MeEndpoint);

        // then: 200 {role:'student', groupName:'ИК-221', login, fullName}.
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await B08Host.ReadJsonObjectAsync(me);
        ResponseAssertions.AssertStringPropertyIs(body, "role", UserRoles.Student);
        ResponseAssertions.AssertStringPropertyIs(body, "groupName", GroupName);
        ResponseAssertions.AssertStringPropertyIs(body, "login", Login);
        ResponseAssertions.AssertStringPropertyIs(body, "fullName", FullName);
    }
}
