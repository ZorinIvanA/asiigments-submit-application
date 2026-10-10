using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-103 «Смена пароля: confirmPassword не совпадает — 400» (negative,
/// FR-016, P1).
///
/// given: currentPassword верный; password 'NewPass1!', confirmPassword
///        'OtherPass2!' (сессия — минтованный access-cookie, ADR-015).
/// when:  PUT /me/password {currentPassword: верный, password:'NewPass1!',
///        confirmPassword:'OtherPass2!'}.
/// then:  400 'Данные заполнены неверно',
///        errors.confirmPassword=['Пароли не совпадают']; пароль не изменён —
///        вход старым паролем даёт 200 (FR-016: валидация
///        password/confirmPassword по FR-006, errors {password, confirmPassword}).
/// </summary>
public sealed class Ts103_PasswordConfirmMismatchTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts103";
    private const string Email = "ts103@x.ru";

    /// <summary>confirmPassword, не совпадающий с password (дословно кейса).</summary>
    private const string OtherConfirmPassword = "OtherPass2!";

    private readonly B17WebAppFactory _factory;

    public Ts103_PasswordConfirmMismatchTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithMismatchedConfirm_Returns400WithConfirmError_PasswordKept()
    {
        // given: пользователь; currentPassword верен (текущий пароль известен).
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-103",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с несовпадающим confirmPassword.
        using var response = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
        {
            currentPassword = B17ProfileHost.TestUserPassword,
            password = B17ProfileHost.NewPassword,
            confirmPassword = OtherConfirmPassword,
        });

        // then: 400 «Данные заполнены неверно»; errors.confirmPassword дословно.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.InvalidData);
        B17BodyAssertions.ErrorFieldEquals(root, "confirmPassword", ErrorTexts.PasswordMismatch);

        // then: пароль не изменён — вход старым паролем даёт 200.
        using var loginClient = B17ProfileHost.Create(_factory);
        using var oldPasswordLogin = await B17ProfileHost.LoginAsync(
            loginClient, Login, B17ProfileHost.TestUserPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 входа старым паролем (пароль не изменён), фактически {(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");
    }
}
