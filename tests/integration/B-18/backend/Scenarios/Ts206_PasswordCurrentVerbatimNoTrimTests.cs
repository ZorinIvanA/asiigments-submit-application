using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-206 «Смена пароля: currentPassword сравнивается дословно, без трима»
/// (boundary, FR-016, P1).
///
/// given: student01 авторизован; его текущий пароль — 'student123!' (без крайних
///        пробелов; DI-сид с реальным PBKDF2-хэшем дословно этой строки).
/// when:  PUT /api/v1/me/password {currentPassword:'student123! ' (хвостовой пробел),
///        password:'NewPass1!', confirmPassword:'NewPass1!'}; затем PUT с дословным
///        currentPassword:'student123!'.
/// then:  первый — 400; message «Неверный текущий пароль»; пароль НЕ применён —
///        второй PUT с дословным currentPassword — 204 (FR-016: «Значение
///        currentPassword сравнивается дословно, без трима (пробел — легальный
///        спецзнак в пределах 128 символов)»).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts206_PasswordCurrentVerbatimNoTrimTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string CurrentPassword = "student123!";
    private const string CurrentPasswordWithTrailingSpace = "student123! ";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts206_PasswordCurrentVerbatimNoTrimTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_CurrentWithTrailingSpaceRejected_ThenVerbatimCurrentAccepted()
    {
        // given: student01 с реальным хэшем пароля 'student123!' (без крайних пробелов).
        var user = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Иванов Иван Иванович 01",
            role: UserRoles.Student,
            groupId: null,
            password: CurrentPassword);
        using var client = B18ProfileHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when №1: currentPassword с хвостовым пробелом (иная строка — трима нет).
        using var trailingSpaceResponse = await client.PutAsJsonAsync(B18ProfileHarness.PasswordEndpoint, new
        {
            currentPassword = CurrentPasswordWithTrailingSpace,
            password = B18ProfileHarness.NewPassword,
            confirmPassword = B18ProfileHarness.NewPassword,
        });

        // then №1: 400 «Неверный текущий пароль» без errors-карты.
        Assert.True(
            trailingSpaceResponse.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 для currentPassword с хвостовым пробелом, фактически {(int)trailingSpaceResponse.StatusCode}: {await trailingSpaceResponse.Content.ReadAsStringAsync()}");
        var trailingSpaceRoot = await B18ProfileAssertions.ReadRootObjectAsync(trailingSpaceResponse);
        B18ProfileAssertions.MessageIs(trailingSpaceRoot, ErrorTexts.WrongCurrentPassword);

        // then №1: пароль НЕ применён — хранимый хэш прежний.
        var storedAfterFirst = B18ProfileHarness.StoredUser(_factory, user.Id);
        Assert.True(
            string.Equals(storedAfterFirst.PasswordHash, user.PasswordHash, StringComparison.Ordinal),
            "Ожидался неизменный хранимый хэш после отклонённой смены (currentPassword с пробелом), фактически хэш изменился.");

        // when №2: повторный PUT с ДОСЛОВНЫМ currentPassword.
        using var verbatimResponse = await client.PutAsJsonAsync(B18ProfileHarness.PasswordEndpoint, new
        {
            currentPassword = CurrentPassword,
            password = B18ProfileHarness.NewPassword,
            confirmPassword = B18ProfileHarness.NewPassword,
        });

        // then №2: 204 — дословное сравнение прошло, пароль сменён.
        Assert.True(
            verbatimResponse.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204 для дословного currentPassword, фактически {(int)verbatimResponse.StatusCode}: {await verbatimResponse.Content.ReadAsStringAsync()}");
    }
}
