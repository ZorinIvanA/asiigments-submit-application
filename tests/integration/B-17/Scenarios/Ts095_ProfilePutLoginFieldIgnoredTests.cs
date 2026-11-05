using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-095 «Профиль: поле login во входе игнорируется» (negative, FR-015, P1).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher», email 'teacher@example.com'); клиент
///        анонимный (аутентификации в приложении нет).
/// when:  PUT /me/profile {fullName:'Новое', email:'teacher@example.com',
///        login:'hacker'}.
/// then:  200; login в ответе 'teacher' — обновление только fullName/email,
///        лишнее поле login во входе игнорируется (FR-015; IF-013: login/роль
///        не редактируются, лишние поля игнорируются).
/// </summary>
public sealed class Ts095_ProfilePutLoginFieldIgnoredTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "teacher";
    private const string Email = "teacher@example.com";
    private const string NewFullName = "Новое";
    private const string IgnoredLoginField = "hacker";

    private readonly B17WebAppFactory _factory;

    public Ts095_ProfilePutLoginFieldIgnoredTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithLoginFieldInBody_IgnoresLogin_Returns200WithOriginalLogin()
    {
        // given: текущий пользователь — засеянный преподаватель (его текущий
        // email — 'teacher@example.com'); клиент анонимный.
        var current = await B17ProfileHost.CurrentUserAsync(_factory);
        Assert.Equal(Email, current.Email);
        using var client = B17ProfileHost.Create(_factory);

        // when: PUT с лишним полем login:'hacker' (свой email — не конфликт).
        using var response = await client.PutAsJsonAsync(B17ProfileHost.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = Email,
            login = IgnoredLoginField,
        });

        // then: 200; login в ответе 'teacher' — поле входа проигнорировано;
        // обновлены только fullName/email.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.StringPropertyIs(root, "login", Login);
        B17BodyAssertions.StringPropertyIs(root, "email", Email);
        B17BodyAssertions.StringPropertyIs(root, "fullName", NewFullName);
        B17BodyAssertions.StringPropertyIs(root, "role", UserRoles.Teacher);

        // then: в хранилище login также не изменён ('hacker' не применён).
        var stored = await B17ProfileHost.StoredUserAsync(_factory, current.Id);
        Assert.True(
            string.Equals(stored.Login, Login, StringComparison.Ordinal),
            $"Ожидался неизменный login «{Login}», фактически «{stored.Login}».");
    }
}
