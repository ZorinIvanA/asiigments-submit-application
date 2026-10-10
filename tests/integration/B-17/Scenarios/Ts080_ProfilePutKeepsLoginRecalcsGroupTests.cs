using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-080 «me/profile PUT: обновляются только fullName/email, groupName
/// пересчитывается» (happy_path, FR-015, P0).
///
/// given: пользователь student01 авторизован (сессия — минтованный access-cookie,
///        ADR-015); новые значения валидны и email свободен; поле login во входе
///        передано (игнорируется); после обновления группа пользователя
///        переименована в 'ИК-999' (DI-правка IGroupRepository — шаг кейса).
/// when:  PUT /me/profile {fullName:'Новое ФИО', email:'new@example.com',
///        login:'hacker'}; затем GET /me/profile.
/// then:  PUT — 200 ProfileDto с новыми значениями; login и роль неизменны
///        ('student01'/'student'); groupName пересчитан по текущему состоянию
///        групп — 'ИК-999' (FR-015 AC «Успешное редактирование»; IF-013:
///        PUT-ответ рендерится по свежему снимку, groupName актуален).
/// </summary>
public sealed class Ts080_ProfilePutKeepsLoginRecalcsGroupTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "student01";
    private const string OldEmail = "student01@example.com";
    private const string OldFullName = "Старое ФИО";
    private const string OriginalGroupName = "ИК-221";
    private const string RenamedGroupName = "ИК-999";
    private const string NewFullName = "Новое ФИО";
    private const string NewEmail = "new@example.com";
    private const string IgnoredLoginField = "hacker";

    private readonly B17WebAppFactory _factory;

    public Ts080_ProfilePutKeepsLoginRecalcsGroupTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_UpdatesOnlyNameEmail_And_RecalculatesGroupName()
    {
        // given: student01 в группе ИК-221; новые значения валидны, email свободен.
        var group = B17ProfileHost.SeedGroup(_factory, OriginalGroupName);
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: OldEmail,
            fullName: OldFullName,
            role: UserRoles.Student,
            groupId: group.Id,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с валидными значениями и лишним полем login:'hacker'.
        using var putResponse = await client.PutAsJsonAsync(B17ProfileHost.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = NewEmail,
            login = IgnoredLoginField,
        });

        // then (PUT): 200; новые fullName/email; login и роль неизменны — поле
        // login во входе игнорируется; в момент PUT группа ещё 'ИК-221'
        // (переименование — последующий шаг кейса).
        Assert.True(
            putResponse.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)putResponse.StatusCode}: {await putResponse.Content.ReadAsStringAsync()}");
        var putBody = await B17BodyAssertions.ReadRootObjectAsync(putResponse);
        B17BodyAssertions.StringPropertyIs(putBody, "fullName", NewFullName);
        B17BodyAssertions.StringPropertyIs(putBody, "email", NewEmail);
        B17BodyAssertions.StringPropertyIs(putBody, "login", Login);
        B17BodyAssertions.StringPropertyIs(putBody, "role", UserRoles.Student);
        B17BodyAssertions.StringPropertyIs(putBody, "groupName", OriginalGroupName);

        // given (продолжение кейса): ПОСЛЕ обновления группа пользователя
        // переименована в 'ИК-999' (DI-правка текущего состояния групп).
        B17ProfileHost.RenameGroup(_factory, group, RenamedGroupName);

        // when (продолжение): GET /me/profile.
        using var getResponse = await client.GetAsync(B17ProfileHost.ProfileEndpoint);

        // then (GET): groupName пересчитан по ТЕКУЩЕМУ состоянию групп —
        // 'ИК-999'; остальные поля — новые значения, login/роль не тронуты.
        Assert.True(
            getResponse.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)getResponse.StatusCode}: {await getResponse.Content.ReadAsStringAsync()}");
        var getBody = await B17BodyAssertions.ReadRootObjectAsync(getResponse);
        B17BodyAssertions.StringPropertyIs(getBody, "login", Login);
        B17BodyAssertions.StringPropertyIs(getBody, "email", NewEmail);
        B17BodyAssertions.StringPropertyIs(getBody, "fullName", NewFullName);
        B17BodyAssertions.StringPropertyIs(getBody, "role", UserRoles.Student);
        B17BodyAssertions.StringPropertyIs(getBody, "groupName", RenamedGroupName);

        // then: в хранилище обновлены только fullName/email — login/роль/группа
        // прежние, поле login:'hacker' проигнорировано.
        var stored = B17ProfileHost.StoredUser(_factory, user.Id);
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
