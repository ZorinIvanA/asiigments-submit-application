using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-096 «Смена пароля: слабый новый пароль — 400 с errors.password»
/// (negative, FR-016, P1; нумерация текущего батча B-16).
///
/// given: currentPassword совпадает с текущим паролем пользователя (DI-сид с
///        реальным PBKDF2-хэшем).
/// when:  PUT /me/password {currentPassword: верный, password:'abc',
///        confirmPassword:'abc'}.
/// then:  400 'Данные заполнены неверно' + errors.password (тексты
///        min/digit/special) (AC FR-016 «Слабый новый пароль»; IF-013 VALIDATION).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts096_PasswordWeakNewPasswordErrorsTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "ts096pwd";
    private const string Email = "ts096pwd@x.ru";
    private const string OldPassword = "OldPass1!";

    private readonly B16WebAppFactory _factory;

    public Ts096_PasswordWeakNewPasswordErrorsTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithWeakNewPassword_Returns400_WithMinDigitSpecialErrors()
    {
        // given: currentPassword совпадает с текущим паролем пользователя.
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-096",
            role: UserRoles.Student,
            groupId: null,
            password: OldPassword);
        using var client = B16Harness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с верным currentPassword и слабым новым паролем 'abc'.
        using var response = await client.PutAsJsonAsync(B16Harness.PasswordEndpoint, new
        {
            currentPassword = OldPassword,
            password = "abc",
            confirmPassword = "abc",
        });

        // then: 400 'Данные заполнены неверно' + errors.password содержит
        // тексты min/digit/special.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.MessageIs(root, ErrorTexts.InvalidData);
        B16Assertions.ErrorFieldContains(
            root,
            "password",
            ErrorTexts.PasswordMin,
            ErrorTexts.PasswordDigit,
            ErrorTexts.PasswordSpecial);
    }
}
