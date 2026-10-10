using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-087 «me/password: слабый новый пароль — пакет ошибок» (negative,
/// FR-016 + FR-006, P0).
///
/// given: currentPassword совпадает с текущим паролем пользователя (DI-сид с
///        реальным PBKDF2-хэшем).
/// when:  PUT {currentPassword: верный, password:'abc', confirmPassword:'abc'}.
/// then:  400 'Данные заполнены неверно' + errors.password (тексты
///        min/digit/special) (FR-016 AC «Слабый новый пароль»: валидация
///        password по FR-006, нарушенные правила — все вместе).
/// </summary>
public sealed class Ts087_PasswordWeakNewPasswordTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts087";
    private const string Email = "ts087@x.ru";

    private readonly B17WebAppFactory _factory;

    public Ts087_PasswordWeakNewPasswordTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithWeakNewPassword_Returns400_WithMinDigitSpecialErrors()
    {
        // given: пользователь с реальным хэшем; currentPassword совпадает с текущим.
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-087",
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

        // then: 400 «Данные заполнены неверно»; errors.password содержит все три текста.
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
    }
}
