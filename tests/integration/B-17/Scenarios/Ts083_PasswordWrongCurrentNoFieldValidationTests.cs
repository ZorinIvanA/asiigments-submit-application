using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-083 «me/password: неверный текущий пароль проверяется до валидации
/// нового» (negative, FR-016, P0).
///
/// given: пользователь авторизован (сессия — минтованный access-cookie, ADR-015)
///        с реальным PBKDF2-хэшем пароля; счётчик KDF «сброшен» — измерение
///        дельтами снимков IKdfCounter.Snapshot() вокруг PUT.
/// when:  PUT /api/v1/me/password {currentPassword:'wrong', password:'abc',
///        confirmPassword:'abc'} — новый пароль заведомо невалиден.
/// then:  400, message 'Неверный текущий пароль', без errors-карты (появление
///        errors.password означало бы обратный порядок проверок); новый пароль
///        НЕ валидировался (Δkdf change_password = 1 — только Verify текущего)
///        и не применён (хэш в хранилище не изменился) — FR-016 AC «Неверный
///        текущий пароль»; IF-013 WRONG_CURRENT_PASSWORD.
/// </summary>
public sealed class Ts083_PasswordWrongCurrentNoFieldValidationTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts083";
    private const string Email = "ts083@x.ru";

    private readonly B17WebAppFactory _factory;

    public Ts083_PasswordWrongCurrentNoFieldValidationTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithWrongCurrent_Returns400WithoutErrors_AndSkipsNewValidation()
    {
        // given: пользователь с реальным PBKDF2-хэшем пароля; сессия минтована.
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-083",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // given: «счётчик KDF сброшен» — база Δkdf ровно вокруг PUT.
        var before = B17ProfileHost.KdfSnapshot(_factory);

        // when: смена с НЕВЕРНЫМ currentPassword и заведомо НЕВАЛИДНОЙ парой
        // нового пароля 'abc'.
        using var response = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
        {
            currentPassword = "wrong",
            password = "abc",
            confirmPassword = "abc",
        });

        // then: 400 «Неверный текущий пароль» БЕЗ errors-карты — полевая
        // валидация нового пароля не выполнялась (иначе errors.password от 'abc'
        // присутствовал бы в ответе).
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.WrongCurrentPassword);
        B17BodyAssertions.ErrorsPropertyIsAbsent(root);

        // then: Δkdf(change_password)=1 — ровно одна деривация Verify текущего
        // пароля; валидация/хэширование нового не выполнялись.
        var after = B17ProfileHost.KdfSnapshot(_factory);
        var changePasswordDelta = B17ProfileHost.KdfCallerDelta(
            before, after, KdfCallers.ChangePassword);
        Assert.True(
            changePasswordDelta == 1,
            $"Ожидался Δkdf(change_password)=1 (только Verify текущего пароля), фактически {changePasswordDelta}.");

        // then: новый пароль не применён — хранимый хэш не изменился.
        var stored = B17ProfileHost.StoredUser(_factory, user.Id);
        Assert.True(
            string.Equals(stored.PasswordHash, user.PasswordHash, StringComparison.Ordinal),
            "Ожидался неизменный хранимый хэш пароля (неверный currentPassword — до " +
            "каких-либо изменений), фактически хэш изменился.");
    }
}
