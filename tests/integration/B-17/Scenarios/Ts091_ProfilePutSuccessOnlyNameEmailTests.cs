using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-091 «Профиль: PUT успех — обновление только fullName/email»
/// (happy_path, FR-015, P0).
///
/// given: пользователь авторизован (сессия — минтованный access-cookie,
///        ADR-015); новые значения валидны и email свободен.
/// when:  PUT /api/v1/me/profile {fullName:'Новое ФИО', email:'new@example.com'};
///        затем группа пользователя переименована (DI-правка текущего состояния
///        групп) и выполнен контрольный GET.
/// then:  200 ProfileDto с новыми значениями; login/role/groupName пользователя
///        не изменились — groupName пересчитан по текущему состоянию групп
///        ('ИК-999' после переименования) (FR-015 AC «Успешное редактирование»;
///        IF-013: PUT-ответ рендерится по свежему снимку после мутации).
/// </summary>
public sealed class Ts091_ProfilePutSuccessOnlyNameEmailTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts091";
    private const string OldEmail = "ts091@x.ru";
    private const string OriginalGroupName = "ИК-221";
    private const string RenamedGroupName = "ИК-999";
    private const string NewFullName = "Новое ФИО";
    private const string NewEmail = "new@example.com";

    private readonly B17WebAppFactory _factory;

    public Ts091_ProfilePutSuccessOnlyNameEmailTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithValidFreeEmail_ReturnsUpdatedDto_KeepsLoginRole_RecalcsGroup()
    {
        // given: пользователь в группе ИК-221; новые значения валидны, email свободен.
        var group = B17ProfileHost.SeedGroup(_factory, OriginalGroupName);
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: OldEmail,
            fullName: "Старое ФИО",
            role: UserRoles.Student,
            groupId: group.Id,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT {fullName:'Новое ФИО', email:'new@example.com'}.
        using var putResponse = await client.PutAsJsonAsync(B17ProfileHost.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = NewEmail,
        });

        // then (PUT): 200 ProfileDto с новыми значениями; login и роль неизменны.
        Assert.True(
            putResponse.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)putResponse.StatusCode}: {await putResponse.Content.ReadAsStringAsync()}");
        var putBody = await B17BodyAssertions.ReadRootObjectAsync(putResponse);
        B17BodyAssertions.StringPropertyIs(putBody, "fullName", NewFullName);
        B17BodyAssertions.StringPropertyIs(putBody, "email", NewEmail);
        B17BodyAssertions.StringPropertyIs(putBody, "login", Login);
        B17BodyAssertions.StringPropertyIs(putBody, "role", UserRoles.Student);

        // given (продолжение кейса): groupName пересчитывается по ТЕКУЩЕМУ
        // состоянию групп — группа пользователя переименована после PUT.
        B17ProfileHost.RenameGroup(_factory, group, RenamedGroupName);

        // when (продолжение): контрольный GET /me/profile.
        using var getResponse = await client.GetAsync(B17ProfileHost.ProfileEndpoint);

        // then (GET): groupName пересчитан ('ИК-999'), остальные поля — новые
        // значения, login/роль не тронуты.
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
        // прежние.
        var stored = B17ProfileHost.StoredUser(_factory, user.Id);
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
