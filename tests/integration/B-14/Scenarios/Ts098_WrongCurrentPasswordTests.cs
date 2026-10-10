using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-098 «Неверный текущий пароль: 400 без полевых ошибок» (negative, FR-022, P0).
///
/// given: сессия пользователя — DI-сид с реальным PBKDF2-хэшем пароля
///        (арбитраж a-017/CR-001), access-cookie минтится харнесом (ADR-022).
/// when:  PUT /me/password {currentPassword:неверный, password:'abc',
///        confirmPassword:'abc'} (новый тоже слаб).
/// then:  400 {"message":"Неверный текущий пароль"}; поле errors отсутствует
///        (без полевых текстов; неверный текущий проверяется ПЕРВОЙ, до полевой
///        валидации нового — IF-013 WRONG_CURRENT). FR-022 AC «Неверный текущий».
/// </summary>
public sealed class Ts098_WrongCurrentPasswordTests : IClassFixture<B14WebAppFactory>
{
    private const string Login = "ts098";
    private const string Email = "ts098@x.ru";
    private const string FullName = "Студент Девяносто Восьмой";

    private readonly B14WebAppFactory _factory;

    public Ts098_WrongCurrentPasswordTests(B14WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task WrongCurrentPassword_Returns400WithoutFieldErrors()
    {
        // given: пользователь с реальным хэшем пароля; сессия — минтованный access-cookie.
        var seeded = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var client = B14Harness.CreateSessionClient(_factory, seeded.Id, UserRoles.Student);

        // when: смена с НЕВЕРНЫМ текущим и заведомо слабым новым.
        using var response = await client.PutAsJsonAsync(B14Harness.PasswordEndpoint, new
        {
            currentPassword = "NotTheRight1!",
            password = "abc",
            confirmPassword = "abc",
        });

        // then: 400 с message «Неверный текущий пароль» дословно и БЕЗ errors
        // (полевая валидация нового не выполняется — ветка WRONG_CURRENT первая).
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B14Assertions.ReadRootObjectAsync(response);
        B14Assertions.MessageIs(root, ErrorTexts.WrongCurrentPassword);
        B14Assertions.ErrorsPropertyIsAbsent(root);
    }
}
