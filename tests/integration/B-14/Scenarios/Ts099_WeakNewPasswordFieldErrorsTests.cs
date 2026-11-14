using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-099 «Слабый новый пароль: полевые ошибки словаря» (boundary, FR-022, P1).
///
/// given: текущий пароль верен — DI-сид пользователя с реальным PBKDF2-хэшем
///        (арбитраж a-017/CR-001); сессия — минтованный access-cookie (ADR-022).
/// when:  PUT /me/password {currentPassword:верный, password:'abcdefgh',
///        confirmPassword:'abcdefgh'}.
/// then:  400 «Данные заполнены неверно»; errors.password СОДЕРЖИТ «Пароль должен
///        содержать хотя бы одну цифру» и «Пароль должен содержать хотя бы один
///        специальный знак» (словарь ошибок валидации). FR-022 AC «Слабый новый».
/// </summary>
public sealed class Ts099_WeakNewPasswordFieldErrorsTests : IClassFixture<B14WebAppFactory>
{
    private const string WeakPassword = "abcdefgh";

    private readonly B14WebAppFactory _factory;

    public Ts099_WeakNewPasswordFieldErrorsTests(B14WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task WeakNewPassword_Returns400_WithPasswordDictionaryErrors()
    {
        // given: пользователь с реальным хэшем пароля; сессия — минтованный access-cookie.
        var seeded = B14Harness.SeedUser(
            _factory,
            login: "ts099",
            email: "ts099@x.ru",
            fullName: "Студент Девяносто Девятый",
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var client = B14Harness.CreateSessionClient(_factory, seeded.Id, UserRoles.Student);

        // when: смена с ВЕРНЫМ текущим и слабым новым (только буквы, без цифр и спецзнаков).
        using var response = await client.PutAsJsonAsync(B14Harness.PasswordEndpoint, new
        {
            currentPassword = B14Harness.TestUserPassword,
            password = WeakPassword,
            confirmPassword = WeakPassword,
        });

        // then: 400 «Данные заполнены неверно»; errors.password содержит оба текста словаря.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B14Assertions.ReadRootObjectAsync(response);
        B14Assertions.MessageIs(root, ErrorTexts.InvalidData);
        B14Assertions.ErrorFieldContains(
            root,
            "password",
            ErrorTexts.PasswordDigit,
            ErrorTexts.PasswordSpecial);
    }
}
