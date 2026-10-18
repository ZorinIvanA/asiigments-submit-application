using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-104 «Смена пароля: currentPassword сравнивается дословно, без трима»
/// (boundary, FR-016, P2).
///
/// given: пользователь с паролем 'Passw0rd! ' (хвостовой пробел — легальный
///        спецзнак; DI-сид с реальным PBKDF2-хэшем ДОСЛОВНО этой строки)
///        авторизован (сессия — минтованный access-cookie, ADR-015).
/// when:  PUT /me/password {currentPassword:'Passw0rd!' (без пробела),
///        password:'NewPass1!', confirmPassword:'NewPass1!'}.
/// then:  400 'Неверный текущий пароль' (FR-016: «Значение currentPassword
///        сравнивается дословно, без трима»; IF-013 WRONG_CURRENT_PASSWORD).
/// </summary>
public sealed class Ts104_PasswordCurrentVerbatimNoTrimTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts104";
    private const string Email = "ts104@x.ru";

    /// <summary>Текущий пароль пользователя с хвостовым пробелом (дословно given).</summary>
    private const string StoredPasswordWithTrailingSpace = "Passw0rd! ";

    /// <summary>Стимул when: та же строка БЕЗ хвостового пробела.</summary>
    private const string CurrentPasswordWithoutSpace = "Passw0rd!";

    private readonly B17WebAppFactory _factory;

    public Ts104_PasswordCurrentVerbatimNoTrimTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithCurrentMissingTrailingSpace_Returns400WrongCurrent()
    {
        // given: пользователь с паролем 'Passw0rd! ' (с хвостовым пробелом),
        // хэш — реальный PBKDF2 дословно этой строки; сессия минтована.
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-104",
            role: UserRoles.Student,
            groupId: null,
            password: StoredPasswordWithTrailingSpace);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с currentPassword 'Passw0rd!' (без хвостового пробела).
        using var response = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
        {
            currentPassword = CurrentPasswordWithoutSpace,
            password = B17ProfileHost.NewPassword,
            confirmPassword = B17ProfileHost.NewPassword,
        });

        // then: 400 «Неверный текущий пароль» — трим не применяется, строка без
        // пробела не совпала с хранимым хэшем.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.WrongCurrentPassword);

        // then: пароль НЕ применён — хранимый хэш прежний.
        var stored = B17ProfileHost.StoredUser(_factory, user.Id);
        Assert.True(
            string.Equals(stored.PasswordHash, user.PasswordHash, StringComparison.Ordinal),
            "Ожидался неизменный хранимый хэш пароля (отклонённая смена), фактически хэш изменился.");
    }
}
