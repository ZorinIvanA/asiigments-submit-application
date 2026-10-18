using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-097 «Смена пароля: неверный текущий пароль — 400 без errors-карты»
/// (negative, FR-016, P0).
///
/// given: пользователь авторизован (пароль 'student123!'; сессия — минтованный
///        access-cookie, ADR-015; DI-сид с реальным PBKDF2-хэшем); счётчик KDF
///        «сброшен» — измерение дельтами снимков IKdfCounter.Snapshot().
/// when:  PUT /api/v1/me/password {currentPassword:'wrong',
///        password:'WeakNew1!', confirmPassword:'WeakNew1!'} — новый пароль
///        намеренно валиден.
/// then:  400, message 'Неверный текущий пароль', без поля errors; новый пароль
///        НЕ валидировался (Δkdf change_password = 1 — только Verify текущего)
///        и не применён (хэш в хранилище не изменился) (FR-016 AC «Неверный
///        текущий пароль»; IF-013 WRONG_CURRENT_PASSWORD).
/// </summary>
public sealed class Ts097_PasswordWrongCurrentNoErrorsMapTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts097";
    private const string Email = "ts097@x.ru";

    /// <summary>Новый пароль when — намеренно валиден (дословно кейса).</summary>
    private const string ValidNewPassword = "WeakNew1!";

    private readonly B17WebAppFactory _factory;

    public Ts097_PasswordWrongCurrentNoErrorsMapTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithWrongCurrent_Returns400WithoutErrors_AndDoesNotApplyNewPassword()
    {
        // given: пользователь с паролем 'student123!' (реальный PBKDF2-хэш); сессия минтована.
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-097",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // given: «счётчик KDF обнулён» — база Δkdf ровно вокруг PUT.
        var before = B17ProfileHost.KdfSnapshot(_factory);

        // when: смена с НЕВЕРНЫМ currentPassword и валидным новым паролем.
        using var response = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
        {
            currentPassword = "wrong",
            password = ValidNewPassword,
            confirmPassword = ValidNewPassword,
        });

        // then: 400 «Неверный текущий пароль» БЕЗ поля errors.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.WrongCurrentPassword);
        B17BodyAssertions.ErrorsPropertyIsAbsent(root);

        // then: новый пароль НЕ валидировался — Δkdf(change_password)=1
        // (ровно одна деривация Verify текущего; валидация/хэширование нового
        // не выполнялись).
        var after = B17ProfileHost.KdfSnapshot(_factory);
        var changePasswordDelta = B17ProfileHost.KdfCallerDelta(
            before, after, KdfCallers.ChangePassword);
        Assert.True(
            changePasswordDelta == 1,
            $"Ожидался Δkdf(change_password)=1 (только Verify текущего пароля), фактически {changePasswordDelta}.");

        // then: новый пароль НЕ применён — хранимый хэш не изменился.
        var stored = B17ProfileHost.StoredUser(_factory, user.Id);
        Assert.True(
            string.Equals(stored.PasswordHash, user.PasswordHash, StringComparison.Ordinal),
            "Ожидался неизменный хранимый хэш пароля (неверный currentPassword — до " +
            "каких-либо изменений), фактически хэш изменился.");
    }
}
