using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-091 «Профиль: PUT успех — обновление только fullName/email»
/// (happy_path, FR-015, P0).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher», role='teacher', группы нет); новые
///        значения валидны и email свободен; клиент анонимный (аутентификации
///        в приложении нет).
/// when:  PUT /api/v1/me/profile {fullName:'Новое ФИО', email:'new@example.com'};
///        затем контрольный GET.
/// then:  200 ProfileDto с новыми значениями; login/role пользователя не
///        изменились, groupName=null (PUT редактирует ТОЛЬКО fullName/email;
///        IF-013: PUT-ответ рендерится по свежему снимку после мутации).
/// </summary>
public sealed class Ts091_ProfilePutSuccessOnlyNameEmailTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "teacher";
    private const string NewFullName = "Новое ФИО";
    private const string NewEmail = "new@example.com";

    private readonly B17WebAppFactory _factory;

    public Ts091_ProfilePutSuccessOnlyNameEmailTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithValidFreeEmail_ReturnsUpdatedDto_KeepsLoginRole()
    {
        // given: текущий пользователь — засеянный преподаватель; новые значения
        // валидны, email свободен; клиент анонимный.
        var current = await B17ProfileHost.CurrentUserAsync(_factory);
        Assert.Equal(Login, current.Login);
        using var client = B17ProfileHost.Create(_factory);

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
        B17BodyAssertions.StringPropertyIs(putBody, "role", UserRoles.Teacher);

        // when (продолжение): контрольный GET /me/profile.
        using var getResponse = await client.GetAsync(B17ProfileHost.ProfileEndpoint);

        // then (GET): те же значения, login/роль не тронуты.
        Assert.True(
            getResponse.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)getResponse.StatusCode}: {await getResponse.Content.ReadAsStringAsync()}");
        var getBody = await B17BodyAssertions.ReadRootObjectAsync(getResponse);
        B17BodyAssertions.StringPropertyIs(getBody, "login", Login);
        B17BodyAssertions.StringPropertyIs(getBody, "email", NewEmail);
        B17BodyAssertions.StringPropertyIs(getBody, "fullName", NewFullName);
        B17BodyAssertions.StringPropertyIs(getBody, "role", UserRoles.Teacher);

        // then: в хранилище обновлены только fullName/email — login/роль/группа
        // прежние.
        var stored = await B17ProfileHost.StoredUserAsync(_factory, current.Id);
        Assert.True(
            string.Equals(stored.Login, Login, StringComparison.Ordinal),
            $"Ожидался неизменный login «{Login}», фактически «{stored.Login}».");
        Assert.True(
            string.Equals(stored.Role, UserRoles.Teacher, StringComparison.Ordinal),
            $"Ожидалась неизменная роль «{UserRoles.Teacher}», фактически «{stored.Role}».");
        Assert.True(
            stored.GroupId is null,
            $"Ожидался неизменный GroupId <null>, фактически {stored.GroupId?.ToString() ?? "<null>"}.");
    }
}
