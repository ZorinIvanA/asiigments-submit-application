using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-098 «Смена пароля: несовпадение confirmPassword» (negative, FR-016, P1).
///
/// given: currentPassword верный; новый пароль валиден (DI-сид с реальным хэшем).
/// when:  PUT /me/password {currentPassword: верный, password:'NewPass1!',
///        confirmPassword:'Other1!x'}.
/// then:  400; message «Данные заполнены неверно»;
///        errors.confirmPassword=['Пароли не совпадают']; пароль не изменён
///        (FR-016: валидация password/confirmPassword по FR-006, errors
///        {password, confirmPassword}; словарь password.mismatch).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts098_PasswordConfirmMismatchTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "ts098";
    private const string Email = "ts098@x.ru";

    private readonly B16WebAppFactory _factory;

    public Ts098_PasswordConfirmMismatchTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithMismatchedConfirm_Returns400_AndKeepsOldHash()
    {
        // given: пользователь с реальным хэшем; currentPassword верный, новый валиден.
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-098",
            role: UserRoles.Student,
            groupId: null,
            password: B16Harness.TestUserPassword);
        using var client = B16Harness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: повтор нового пароля не совпадает.
        using var response = await client.PutAsJsonAsync(B16Harness.PasswordEndpoint, new
        {
            currentPassword = B16Harness.TestUserPassword,
            password = B16Harness.NewPassword,
            confirmPassword = "Other1!x",
        });

        // then: 400 «Данные заполнены неверно»; errors.confirmPassword дословно.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.MessageIs(root, ErrorTexts.InvalidData);
        B16Assertions.ErrorFieldEquals(root, "confirmPassword", ErrorTexts.PasswordMismatch);

        // then: пароль не изменён — хранимый хэш прежний.
        var stored = B16Harness.StoredUser(_factory, user.Id);
        Assert.True(
            string.Equals(stored.PasswordHash, user.PasswordHash, StringComparison.Ordinal),
            "Ожидался неизменный хранимый хэш пароля (несовпадение повтора — смена не выполняется), фактически хэш изменился.");
    }
}
