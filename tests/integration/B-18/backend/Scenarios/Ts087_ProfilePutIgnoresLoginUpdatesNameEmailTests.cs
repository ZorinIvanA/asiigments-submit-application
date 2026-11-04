using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-087 «Профиль: PUT обновляет только fullName и email» (happy_path, FR-015, P0).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher», role='teacher'); новые значения валидны
///        и email свободен; в теле запроса дополнительно передано поле
///        login:'hacker'; клиент анонимный (аутентификации в приложении нет).
/// when:  PUT /api/v1/me/profile {fullName:'Новое ФИО', email:'new@example.com',
///        login:'hacker'}.
/// then:  200; ProfileDto с новыми fullName и email; login и роль неизменны (поле
///        login во входе игнорируется); groupName=null (PUT группы не касается;
///        FR-015 AC «Успешное редактирование»; IF-013).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts087_ProfilePutIgnoresLoginUpdatesNameEmailTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "teacher";
    private const string NewFullName = "Новое ФИО";
    private const string NewEmail = "new@example.com";
    private const string IntruderLogin = "hacker";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts087_ProfilePutIgnoresLoginUpdatesNameEmailTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_UpdatesOnlyFullNameAndEmail_IgnoresLoginField()
    {
        // given: текущий пользователь — засеянный преподаватель; новые значения
        // валидны, email свободен; клиент анонимный.
        var current = await B18ProfileHarness.CurrentUserAsync(_factory);
        Assert.Equal(Login, current.Login);
        using var client = B18ProfileHarness.Create(_factory);

        // when: PUT с валидными значениями и ЛИШНИМ полем login:'hacker'.
        using var response = await client.PutAsJsonAsync(B18ProfileHarness.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = NewEmail,
            login = IntruderLogin,
        });

        // then: 200; новые fullName/email; login и роль неизменны.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B18ProfileAssertions.ReadRootObjectAsync(response);
        B18ProfileAssertions.StringPropertyIs(root, "fullName", NewFullName);
        B18ProfileAssertions.StringPropertyIs(root, "email", NewEmail);
        B18ProfileAssertions.StringPropertyIs(root, "login", Login);
        B18ProfileAssertions.StringPropertyIs(root, "role", UserRoles.Teacher);

        // then: в хранилище обновлены только fullName/email — login/роль прежние,
        // поле login:'hacker' проигнорировано.
        var stored = await B18ProfileHarness.StoredUserAsync(_factory, current.Id);
        Assert.True(
            string.Equals(stored.Login, Login, StringComparison.Ordinal),
            $"Ожидался неизменный login «{Login}», фактически «{stored.Login}».");
        Assert.True(
            string.Equals(stored.FullName, NewFullName, StringComparison.Ordinal),
            $"Ожидался обновлённый fullName «{NewFullName}», фактически «{stored.FullName}».");
        Assert.True(
            string.Equals(stored.Email, NewEmail, StringComparison.Ordinal),
            $"Ожидался обновлённый email «{NewEmail}», фактически «{stored.Email}».");
        Assert.True(
            string.Equals(stored.Role, UserRoles.Teacher, StringComparison.Ordinal),
            $"Ожидалась неизменная роль «{UserRoles.Teacher}», фактически «{stored.Role}».");
    }
}
