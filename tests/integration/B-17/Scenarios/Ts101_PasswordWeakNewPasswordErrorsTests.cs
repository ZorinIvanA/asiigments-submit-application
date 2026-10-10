using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-101 «Смена пароля: слабый новый пароль — 400 с errors» (negative,
/// FR-016 + FR-006, P0).
///
/// given: currentPassword совпадает с текущим паролем (DI-сид с реальным
///        PBKDF2-хэшем; сессия — минтованный access-cookie, ADR-015).
/// when:  PUT /me/password {currentPassword:'student123!', password:'abc',
///        confirmPassword:'abc'}.
/// then:  400 'Данные заполнены неверно' + errors.password (тексты
///        min/digit/special); пароль не изменён — вход старым паролем даёт 200
///        (FR-016 AC «Слабый новый пароль»: валидация password по FR-006,
///        нарушенные правила — все вместе).
/// </summary>
public sealed class Ts101_PasswordWeakNewPasswordErrorsTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts101";
    private const string Email = "ts101@x.ru";

    private readonly B17WebAppFactory _factory;

    public Ts101_PasswordWeakNewPasswordErrorsTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithWeakNewPassword_Returns400WithMinDigitSpecialErrors_PasswordKept()
    {
        // given: пользователь; currentPassword совпадает с текущим паролем.
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-101",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: смена со слабым новым паролем 'abc'.
        using var response = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
        {
            currentPassword = B17ProfileHost.TestUserPassword,
            password = "abc",
            confirmPassword = "abc",
        });

        // then: 400 «Данные заполнены неверно»; errors.password содержит все
        // три текста (min/digit/special).
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.InvalidData);
        B17BodyAssertions.ErrorFieldContains(
            root,
            "password",
            ErrorTexts.PasswordMin,
            ErrorTexts.PasswordDigit,
            ErrorTexts.PasswordSpecial);

        // then: пароль не изменён — вход старым паролем даёт 200.
        using var loginClient = B17ProfileHost.Create(_factory);
        using var oldPasswordLogin = await B17ProfileHost.LoginAsync(
            loginClient, Login, B17ProfileHost.TestUserPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 входа старым паролем (пароль не изменён), фактически {(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");
    }
}
