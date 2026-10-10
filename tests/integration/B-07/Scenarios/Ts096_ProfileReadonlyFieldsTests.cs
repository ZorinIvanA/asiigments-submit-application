using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-096 «login/role/groupId через PUT /me/profile не редактируются»
/// (negative, FR-021, P1).
///
/// given: сессия студента с login='st01', role='student', groupId=X
///        (учётка регистрируется публичным API; группа X и привязка студента
///        создаются DI-сиддингом через IGroupRepository/IUserRepository тестового
///        хоста — контракт IF-015, ADR-010).
/// when:  PUT /me/profile {fullName:'Имя', email:свой, login:'hacker',
///        role:'teacher', groupId:null}.
/// then:  200; в ответе и хранилище login='st01', role='student', groupId=X
///        (лишние поля игнорируются). FR-021: «login/role/groupId не
///        редактируются, лишние поля игнорируются».
/// </summary>
public sealed class Ts096_ProfileReadonlyFieldsTests : IClassFixture<B07WebAppFactory>
{
    private const string Login = "st01";
    private const string Email = "st96@x.ru";
    private const string FullName = "Имя";

    private readonly B07WebAppFactory _factory;

    public Ts096_ProfileReadonlyFieldsTests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_IgnoresLoginRoleGroupId()
    {
        // given: студент st01 (public API), затем группа X и привязка студента (DI).
        await HostClients.RegisterStudentAsync(
            _factory, fullName: FullName, login: Login, email: Email);

        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var groups = _factory.Services.GetRequiredService<IGroupRepository>();

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Группа X B-07",
            CreatedAt = DateTime.UtcNow,
        };
        groups.Add(group);

        var student = users.GetByLogin(Login);
        Assert.NotNull(student);
        Assert.Equal(UserRoles.Student, student.Role);
        student.GroupId = group.Id;
        users.Update(student);

        // given проверен: login='st01', role='student', groupId=X.
        var givenUser = users.GetByLogin(Login);
        Assert.NotNull(givenUser);
        Assert.Equal(UserRoles.Student, givenUser.Role);
        Assert.Equal(group.Id, givenUser.GroupId);

        // given: сессия студента.
        var client = HostClients.Create(_factory);
        await HostClients.LoginAsync(client, Login);

        // when: профиль с попытками отредактировать login/role/groupId.
        using var response = await client.PutAsJsonAsync(HostClients.ProfileEndpoint, new
        {
            fullName = FullName,
            email = Email,
            login = "hacker",
            role = "teacher",
            groupId = (Guid?)null,
        });

        // then: 200 — лишние поля игнорируются.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.StringPropertyIs(root, "login", "st01");
        BodyAssertions.StringPropertyIs(root, "role", "student");
        BodyAssertions.StringPropertyIs(root, "fullName", FullName);
        BodyAssertions.StringPropertyIs(root, "email", Email);

        // then: и в хранилище login/role/groupId не изменились.
        var stored = users.GetById(givenUser.Id);
        Assert.NotNull(stored);
        Assert.Equal("st01", stored.Login);
        Assert.Equal(UserRoles.Student, stored.Role);
        Assert.Equal(group.Id, stored.GroupId);
        Assert.Equal(Email, stored.Email);
    }
}
