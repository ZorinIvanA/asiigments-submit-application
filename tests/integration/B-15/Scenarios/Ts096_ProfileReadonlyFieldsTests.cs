using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-096 «login/role/groupId через PUT /me/profile не редактируются» (negative, FR-021, P1).
///
/// given: группа X создана DI-сидом IGroupRepository; студент login='st01',
///        role='student', groupId=X, email свой — DI-сид IUserRepository тестового
///        хоста; сессия студента — cookie access_token с JWT HS256, минтым харнесом
///        ключом Auth__JwtKey тестового хоста; POST /auth/register и POST /auth/login
///        в предусловиях НЕ вызываются (CR-001, арбитраж a-017).
/// when:  PUT /me/profile {fullName:'Имя', email:свой, login:'hacker',
///        role:'teacher', groupId:null}.
/// then:  200; в ответе и хранилище login='st01', role='student', groupId=X
///        (лишние поля игнорируются). FR-021: «login/role/groupId не редактируются,
///        лишние поля игнорируются».
/// </summary>
public sealed class Ts096_ProfileReadonlyFieldsTests : IClassFixture<B15WebAppFactory>
{
    private const string GroupName = "X";
    private const string Login = "st01";
    private const string Email = "st01@x.ru";

    private readonly B15WebAppFactory _factory;

    public Ts096_ProfileReadonlyFieldsTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_IgnoresReadonlyFields()
    {
        // given: группа X и студент st01 в ней (DI-сид); сессия студента.
        var group = B15Harness.SeedGroup(_factory, GroupName);
        var seeded = B15Harness.SeedStudent(
            _factory, fullName: "Текущее Имя", login: Login, email: Email, groupId: group.Id);
        using var client = B15Harness.CreateSessionClient(_factory, seeded.Id);

        // when: попытка отредактировать login/role/groupId лишними полями.
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = "Имя",
            email = Email,
            login = "hacker",
            role = "teacher",
            groupId = (Guid?)null,
        });

        // then: 200; в ответе login/role не изменились, группа осталась (groupName=X —
        // транспортная форма groupId в ProfileDto).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.StringPropertyIs(root, "login", Login);
        BodyAssertions.StringPropertyIs(root, "role", "student");
        BodyAssertions.StringPropertyIs(root, "groupName", GroupName);

        // then: и в хранилище login='st01', role='student', groupId=X.
        var stored = B15Harness.UserById(_factory, seeded.Id);
        Assert.Equal(Login, stored.Login);
        Assert.Equal("student", stored.Role);
        Assert.Equal(group.Id, stored.GroupId);
    }
}
