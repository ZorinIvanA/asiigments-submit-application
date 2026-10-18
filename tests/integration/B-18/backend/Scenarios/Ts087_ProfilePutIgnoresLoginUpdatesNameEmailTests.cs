using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-087 «Профиль: PUT обновляет только fullName и email» (happy_path, FR-015, P0).
///
/// given: пользователь авторизован; новые значения валидны и email свободен;
///        в теле запроса дополнительно передано поле login:'hacker'.
/// when:  PUT /api/v1/me/profile {fullName:'Новое ФИО', email:'new@example.com',
///        login:'hacker'}.
/// then:  200; ProfileDto с новыми fullName и email; login и роль неизменны (поле
///        login во входе игнорируется); groupName пересчитан (FR-015 AC «Успешное
///        редактирование»; IF-013: groupName вычисляется по ТЕКУЩЕМУ состоянию
///        групп — наблюдаемость: группа пользователя переименована DI-сидом между
///        сидом пользователя и PUT, ответ обязан показать новое имя).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts087_ProfilePutIgnoresLoginUpdatesNameEmailTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "ts087";
    private const string Email = "ts087@example.com";
    private const string OriginalFullName = "Студент ТС-087";
    private const string GroupName = "Группа Восемьдесят Семь";
    private const string GroupRenamed = "Группа Восемьдесят Семь (переименована)";
    private const string NewFullName = "Новое ФИО";
    private const string NewEmail = "new@example.com";
    private const string IntruderLogin = "hacker";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts087_ProfilePutIgnoresLoginUpdatesNameEmailTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_UpdatesOnlyFullNameAndEmail_IgnoresLoginField_AndRecomputesGroupName()
    {
        // given: пользователь с группой; новые значения валидны, email свободен.
        var group = B18ProfileHarness.SeedGroup(_factory, GroupName);
        var user = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: OriginalFullName,
            role: UserRoles.Student,
            groupId: group.Id,
            password: B18ProfileHarness.TestUserPassword);

        // given (наблюдаемость «groupName пересчитан»): состояние групп изменилось ДО PUT.
        B18ProfileHarness.RenameGroup(_factory, group, GroupRenamed);

        using var client = B18ProfileHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с валидными значениями и ЛИШНИМ полем login:'hacker'.
        using var response = await client.PutAsJsonAsync(B18ProfileHarness.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = NewEmail,
            login = IntruderLogin,
        });

        // then: 200; новые fullName/email; login и роль неизменны; groupName пересчитан.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B18ProfileAssertions.ReadRootObjectAsync(response);
        B18ProfileAssertions.StringPropertyIs(root, "fullName", NewFullName);
        B18ProfileAssertions.StringPropertyIs(root, "email", NewEmail);
        B18ProfileAssertions.StringPropertyIs(root, "login", Login);
        B18ProfileAssertions.StringPropertyIs(root, "role", UserRoles.Student);
        B18ProfileAssertions.StringPropertyIs(root, "groupName", GroupRenamed);

        // then: в хранилище обновлены только fullName/email — login/роль/группа прежние,
        // поле login:'hacker' проигнорировано.
        var stored = B18ProfileHarness.StoredUser(_factory, user.Id);
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
            string.Equals(stored.Role, UserRoles.Student, StringComparison.Ordinal),
            $"Ожидалась неизменная роль «{UserRoles.Student}», фактически «{stored.Role}».");
        Assert.True(
            stored.GroupId == group.Id,
            $"Ожидался неизменный GroupId {group.Id}, фактически {stored.GroupId?.ToString() ?? "<null>"}.");
    }
}
