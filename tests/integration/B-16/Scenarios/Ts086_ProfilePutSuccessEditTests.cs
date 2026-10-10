using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-086 «Профиль: успешное редактирование» (happy_path, FR-015, P0;
/// нумерация текущего батча B-16).
///
/// given: пользователь авторизован; новые значения валидны; email
///        'new@example.com' свободен (свежая фикстура — пустое хранилище,
///        кроме сид-преподавателя: email заведомо свободен).
/// when:  PUT /api/v1/me/profile {fullName:'Новое ФИО', email:'new@example.com'}.
/// then:  200 ProfileDto с новыми значениями; login, role, группа неизменны
///        (AC FR-015 «Успешное редактирование»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts086_ProfilePutSuccessEditTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "ts086profile";
    private const string OldEmail = "old@example.com";
    private const string OldFullName = "Старое ФИО";
    private const string GroupName = "ИК-221";
    private const string NewFullName = "Новое ФИО";
    private const string NewEmail = "new@example.com";

    private readonly B16WebAppFactory _factory;

    public Ts086_ProfilePutSuccessEditTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithValidNewValues_ReturnsUpdatedDto_AndKeepsLoginRoleGroup()
    {
        // given: пользователь авторизован (в группе — чтобы «группа неизменны»
        // проверялось по значению, а не по null→null); email 'new@example.com'
        // свободен (хранилище фикстуры пусто, кроме сид-преподавателя).
        var group = B16Harness.SeedGroup(_factory, GroupName);
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: OldEmail,
            fullName: OldFullName,
            role: UserRoles.Student,
            groupId: group.Id,
            password: B16Harness.TestUserPassword);
        using var client = B16Harness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT {fullName:'Новое ФИО', email:'new@example.com'}.
        using var response = await client.PutAsJsonAsync(B16Harness.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = NewEmail,
        });

        // then: 200 ProfileDto с новыми значениями; login, role, группа неизменны.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.StringPropertyIs(root, "fullName", NewFullName);
        B16Assertions.StringPropertyIs(root, "email", NewEmail);
        B16Assertions.StringPropertyIs(root, "login", Login);
        B16Assertions.StringPropertyIs(root, "role", UserRoles.Student);
        B16Assertions.StringPropertyIs(root, "groupName", GroupName);

        // then: в хранилище — новые fullName/email; login/роль/группа прежние.
        var stored = B16Harness.StoredUser(_factory, user.Id);
        Assert.True(
            string.Equals(stored.FullName, NewFullName, StringComparison.Ordinal),
            $"Ожидался обновлённый fullName «{NewFullName}», фактически «{stored.FullName}».");
        Assert.True(
            string.Equals(stored.Email, NewEmail, StringComparison.Ordinal),
            $"Ожидался обновлённый email «{NewEmail}», фактически «{stored.Email}».");
        Assert.True(
            string.Equals(stored.Login, Login, StringComparison.Ordinal),
            $"Ожидался неизменный login «{Login}», фактически «{stored.Login}».");
        Assert.True(
            string.Equals(stored.Role, UserRoles.Student, StringComparison.Ordinal),
            $"Ожидалась неизменная роль «{UserRoles.Student}», фактически «{stored.Role}».");
        Assert.True(
            stored.GroupId == group.Id,
            $"Ожидался неизменный GroupId {group.Id}, фактически {stored.GroupId?.ToString() ?? "<null>"}.");
    }
}
