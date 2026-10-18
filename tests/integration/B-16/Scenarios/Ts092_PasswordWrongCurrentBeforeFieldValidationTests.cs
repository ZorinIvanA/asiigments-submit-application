using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-092 «Смена пароля: неверный текущий пароль — 400 без errors, раньше
/// полевых ошибок» (negative, FR-016, P0; нумерация текущего батча B-16).
///
/// given: пользователь (пароль 'OldPass1!') авторизован (DI-сид с реальным
///        PBKDF2-хэшем; сессия — минтованный access-cookie, ADR-015).
/// when:  PUT /api/v1/me/password {currentPassword:'wrong', password:'abc',
///        confirmPassword:'abc'} — новый пароль заведомо слаб.
/// then:  400, message 'Неверный текущий пароль', без errors-карты; новый
///        пароль НЕ валидировался и не применён (слабый 'abc' не дал VALIDATION —
///        проверка currentPassword до полевых ошибок) (AC FR-016 «Неверный
///        текущий пароль»; IF-013 WRONG_CURRENT_PASSWORD).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts092_PasswordWrongCurrentBeforeFieldValidationTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "ts092pwd";
    private const string Email = "ts092pwd@x.ru";
    private const string OldPassword = "OldPass1!";

    private readonly B16WebAppFactory _factory;

    public Ts092_PasswordWrongCurrentBeforeFieldValidationTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithWrongCurrent_Returns400WithoutErrors_AndDoesNotApplyNewPassword()
    {
        // given: пользователь (пароль 'OldPass1!') авторизован.
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-092",
            role: UserRoles.Student,
            groupId: null,
            password: OldPassword);
        using var client = B16Harness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с неверным currentPassword и заведомо слабым новым 'abc'.
        using var response = await client.PutAsJsonAsync(B16Harness.PasswordEndpoint, new
        {
            currentPassword = "wrong",
            password = "abc",
            confirmPassword = "abc",
        });

        // then: 400 'Неверный текущий пароль' БЕЗ errors-карты — слабый 'abc'
        // не дал VALIDATION: проверка currentPassword раньше полевых ошибок.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.MessageIs(root, ErrorTexts.WrongCurrentPassword);
        B16Assertions.ErrorsPropertyIsAbsent(root);

        // then: новый пароль НЕ применён — хранимый хэш не изменился.
        var stored = B16Harness.StoredUser(_factory, user.Id);
        Assert.True(
            string.Equals(stored.PasswordHash, user.PasswordHash, StringComparison.Ordinal),
            "Ожидался неизменный хранимый хэш пароля (неверный currentPassword — до каких-либо изменений), фактически хэш изменился.");
    }
}
