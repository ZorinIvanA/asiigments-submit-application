using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-095 «Профиль: поле login во входе игнорируется» (negative, FR-015, P1).
///
/// given: пользователь student01 авторизован (сессия — минтованный
///        access-cookie, ADR-015).
/// when:  PUT /me/profile {fullName:'Новое', email:'student01@example.com',
///        login:'hacker'}.
/// then:  200; login в ответе 'student01' — обновление только fullName/email,
///        лишнее поле login во входе игнорируется (FR-015; IF-013: login/роль
///        не редактируются, лишние поля игнорируются).
/// </summary>
public sealed class Ts095_ProfilePutLoginFieldIgnoredTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string NewFullName = "Новое";
    private const string IgnoredLoginField = "hacker";

    private readonly B17WebAppFactory _factory;

    public Ts095_ProfilePutLoginFieldIgnoredTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithLoginFieldInBody_IgnoresLogin_Returns200WithOriginalLogin()
    {
        // given: student01 авторизован (его текущий email — 'student01@example.com').
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Иванов Иван Иванович 01",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с лишним полем login:'hacker' (свой email — не конфликт).
        using var response = await client.PutAsJsonAsync(B17ProfileHost.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = Email,
            login = IgnoredLoginField,
        });

        // then: 200; login в ответе 'student01' — поле входа проигнорировано;
        // обновлены только fullName/email.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.StringPropertyIs(root, "login", Login);
        B17BodyAssertions.StringPropertyIs(root, "email", Email);
        B17BodyAssertions.StringPropertyIs(root, "fullName", NewFullName);
        B17BodyAssertions.StringPropertyIs(root, "role", UserRoles.Student);

        // then: в хранилище login также не изменён ('hacker' не применён).
        var stored = B17ProfileHost.StoredUser(_factory, user.Id);
        Assert.True(
            string.Equals(stored.Login, Login, StringComparison.Ordinal),
            $"Ожидался неизменный login «{Login}», фактически «{stored.Login}».");
    }
}
