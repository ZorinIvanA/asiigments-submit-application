using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

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
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts098_PasswordConfirmMismatchTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "ts098";
    private const string Email = "ts098@x.ru";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts098_PasswordConfirmMismatchTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithMismatchedConfirm_Returns400_AndKeepsOldHash()
    {
        // given: пользователь с реальным хэшем; currentPassword верный, новый валиден.
        var user = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-098",
            role: UserRoles.Student,
            groupId: null,
            password: B18ProfileHarness.TestUserPassword);
        using var client = B18ProfileHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: повтор нового пароля не совпадает.
        using var response = await client.PutAsJsonAsync(B18ProfileHarness.PasswordEndpoint, new
        {
            currentPassword = B18ProfileHarness.TestUserPassword,
            password = B18ProfileHarness.NewPassword,
            confirmPassword = "Other1!x",
        });

        // then: 400 «Данные заполнены неверно»; errors.confirmPassword дословно.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B18ProfileAssertions.ReadRootObjectAsync(response);
        B18ProfileAssertions.MessageIs(root, ErrorTexts.InvalidData);
        B18ProfileAssertions.ErrorFieldEquals(root, "confirmPassword", ErrorTexts.PasswordMismatch);

        // then: пароль не изменён — хранимый хэш прежний.
        var stored = B18ProfileHarness.StoredUser(_factory, user.Id);
        Assert.True(
            string.Equals(stored.PasswordHash, user.PasswordHash, StringComparison.Ordinal),
            "Ожидался неизменный хранимый хэш пароля (несовпадение повтора — смена не выполняется), фактически хэш изменился.");
    }
}
