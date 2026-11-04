using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-086 «Профиль: успешное редактирование» (happy_path, FR-015, P0;
/// нумерация текущего батча B-16).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher», email 'teacher@example.com'); новые
///        значения валидны; email 'new@example.com' свободен (свежая фикстура —
///        пустое хранилище, кроме сид-преподавателя); клиент анонимный
///        (аутентификации в приложении нет).
/// when:  PUT /api/v1/me/profile {fullName:'Новое ФИО', email:'new@example.com'}.
/// then:  200 ProfileDto с новыми значениями; login, role неизменны
///        (AC FR-015 «Успешное редактирование»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts086_ProfilePutSuccessEditTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "teacher";
    private const string NewFullName = "Новое ФИО";
    private const string NewEmail = "new@example.com";

    private readonly B16WebAppFactory _factory;

    public Ts086_ProfilePutSuccessEditTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithValidNewValues_ReturnsUpdatedDto_AndKeepsLoginRole()
    {
        // given: текущий пользователь — засеянный преподаватель; email
        // 'new@example.com' свободен; клиент анонимный.
        var current = await B16Harness.CurrentUserAsync(_factory);
        using var client = B16Harness.Create(_factory);

        // when: PUT {fullName:'Новое ФИО', email:'new@example.com'}.
        using var response = await client.PutAsJsonAsync(B16Harness.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = NewEmail,
        });

        // then: 200 ProfileDto с новыми значениями; login и роль неизменны.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.StringPropertyIs(root, "fullName", NewFullName);
        B16Assertions.StringPropertyIs(root, "email", NewEmail);
        B16Assertions.StringPropertyIs(root, "login", Login);
        B16Assertions.StringPropertyIs(root, "role", UserRoles.Teacher);
        Assert.True(
            root.TryGetProperty("groupName", out var groupName) && groupName.ValueKind == System.Text.Json.JsonValueKind.Null,
            $"Ожидался groupName=null (группа не редактируется PUT /me/profile), фактически: {root.GetRawText()}");

        // then: в хранилище — новые fullName/email; login/роль/группа прежние.
        var stored = await B16Harness.StoredUserAsync(_factory, current.Id);
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
            string.Equals(stored.Role, UserRoles.Teacher, StringComparison.Ordinal),
            $"Ожидалась неизменная роль «{UserRoles.Teacher}», фактически «{stored.Role}».");
        Assert.True(
            stored.GroupId is null,
            $"Ожидался неизменный GroupId <null>, фактически {stored.GroupId?.ToString() ?? "<null>"}.");
    }
}
