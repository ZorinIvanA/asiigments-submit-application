using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-097 «Смена пароля: слабый новый пароль — errors.password» (negative, FR-016, P0).
///
/// given: currentPassword совпадает с текущим паролем (DI-сид с реальным
///        PBKDF2-хэшем).
/// when:  PUT /me/password {currentPassword: верный, password:'abc',
///        confirmPassword:'abc'}.
/// then:  400; message «Данные заполнены неверно»; errors.password содержит
///        «Пароль должен содержать не менее 8 символов», «Пароль должен содержать
///        хотя бы одну цифру», «Пароль должен содержать хотя бы один специальный
///        знак» (FR-016 AC «Слабый новый пароль»: валидация password по FR-006,
///        нарушенные правила — все вместе).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts097_PasswordWeakNewPasswordTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "ts097";
    private const string Email = "ts097@x.ru";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts097_PasswordWeakNewPasswordTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithWeakNewPassword_Returns400_WithMinDigitSpecialErrors()
    {
        // given: пользователь с реальным хэшем; currentPassword совпадает с текущим.
        var user = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-097",
            role: UserRoles.Student,
            groupId: null,
            password: B18ProfileHarness.TestUserPassword);
        using var client = B18ProfileHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: смена со слабым новым паролем 'abc'.
        using var response = await client.PutAsJsonAsync(B18ProfileHarness.PasswordEndpoint, new
        {
            currentPassword = B18ProfileHarness.TestUserPassword,
            password = "abc",
            confirmPassword = "abc",
        });

        // then: 400 «Данные заполнены неверно»; errors.password содержит все три текста.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B18ProfileAssertions.ReadRootObjectAsync(response);
        B18ProfileAssertions.MessageIs(root, ErrorTexts.InvalidData);
        B18ProfileAssertions.ErrorFieldContains(
            root,
            "password",
            ErrorTexts.PasswordMin,
            ErrorTexts.PasswordDigit,
            ErrorTexts.PasswordSpecial);
    }
}
