using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-181 «me/password: currentPassword сравнивается дословно, без трима»
/// (boundary, FR-016, P1; ISS-004(г) скептика: пробельный паддинг — легальный
/// спецзнак по FR-016 — прежде не предъявлялся; кейс фальсифицирует трим
/// текущего пароля).
///
/// given: пользователь авторизован (сессия — минтованный access-cookie,
///        ADR-015); его текущий пароль — 'student123!' БЕЗ крайних пробелов
///        (DI-сид с реальным PBKDF2-хэшем дословно этой строки); новый пароль
///        'NewPass1!' валиден; счётчик KDF сбрасывается перед каждым запросом —
///        дельты снимков IKdfCounter.Snapshot().
/// when:  PUT /api/v1/me/password {currentPassword:'student123! ' (хвостовой
///        пробел — ДРУГАЯ строка), password:'NewPass1!', confirmPassword:
///        'NewPass1!'}; затем контрольный PUT с дословным currentPassword:
///        'student123!' и тем же новым паролем.
/// then:  первый — 400, message «Неверный текущий пароль», без errors-карты;
///        Δkdf=1 (ровно одна деривация — Verify дословной строки с хвостовым
///        пробелом против хранимого хэша; трим не применяется); пароль НЕ
///        изменён — контрольный PUT с дословным currentPassword — 204, вход по
///        'NewPass1!' после него — 200 (FR-016: «Значение currentPassword
///        сравнивается дословно, без трима»; арифметика KDF — паритет с веткой
///        неверного пароля TS-083).
/// </summary>
public sealed class Ts181_PasswordCurrentVerbatimNoTrimTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts181";
    private const string Email = "ts181@x.ru";

    /// <summary>Текущий пароль пользователя БЕЗ крайних пробелов (дословно кейса).</summary>
    private const string CurrentPasswordVerbatim = "student123!";

    /// <summary>Стимул when: ТА ЖЕ строка с хвостовым пробелом — ДРУГАЯ строка.</summary>
    private const string CurrentPasswordWithTrailingSpace = "student123! ";

    private readonly B17WebAppFactory _factory;

    public Ts181_PasswordCurrentVerbatimNoTrimTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_CurrentWithTrailingSpaceRejectedNoTrim_ThenVerbatimCurrentAccepted()
    {
        // given: пользователь с реальным PBKDF2-хэшем пароля 'student123!'
        // (без крайних пробелов); сессия минтована.
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-181",
            role: UserRoles.Student,
            groupId: null,
            password: CurrentPasswordVerbatim);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // given: «счётчик KDF сбрасывается перед запросом» — база Δkdf.
        var before = B17ProfileHost.KdfSnapshot(_factory);

        // when №1: PUT с currentPassword, содержащим хвостовой пробел
        // (иная строка — трим не применяется).
        using var trailingSpaceResponse = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
        {
            currentPassword = CurrentPasswordWithTrailingSpace,
            password = B17ProfileHost.NewPassword,
            confirmPassword = B17ProfileHost.NewPassword,
        });

        // then №1: 400 «Неверный текущий пароль» без errors-карты.
        Assert.True(
            trailingSpaceResponse.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 для currentPassword с хвостовым пробелом, фактически {(int)trailingSpaceResponse.StatusCode}: {await trailingSpaceResponse.Content.ReadAsStringAsync()}");
        var trailingSpaceRoot = await B17BodyAssertions.ReadRootObjectAsync(trailingSpaceResponse);
        B17BodyAssertions.MessageIs(trailingSpaceRoot, ErrorTexts.WrongCurrentPassword);
        B17BodyAssertions.ErrorsPropertyIsAbsent(trailingSpaceRoot);

        // then №1: Δkdf=1 — ровно одна деривация: Verify ДОСЛОВНОЙ строки
        // с хвостовым пробелом против хранимого хэша (трим не применяется).
        var afterFirst = B17ProfileHost.KdfSnapshot(_factory);
        var firstDelta = B17ProfileHost.KdfTotalDelta(before, afterFirst);
        Assert.True(
            firstDelta == 1,
            $"Ожидался Δkdf=1 (Verify дословной строки с хвостовым пробелом), фактически {firstDelta}.");

        // then №1: пароль НЕ изменён — хранимый хэш прежний.
        var storedAfterFirst = B17ProfileHost.StoredUser(_factory, user.Id);
        Assert.True(
            string.Equals(storedAfterFirst.PasswordHash, user.PasswordHash, StringComparison.Ordinal),
            "Ожидался неизменный хранимый хэш после отклонённой смены (currentPassword с пробелом), фактически хэш изменился.");

        // given: «счётчик KDF сбрасывается перед каждым запросом» — новая база Δkdf.
        var beforeControl = B17ProfileHost.KdfSnapshot(_factory);

        // when №2: контрольный PUT с ДОСЛОВНЫМ currentPassword и тем же новым паролем.
        using var verbatimResponse = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
        {
            currentPassword = CurrentPasswordVerbatim,
            password = B17ProfileHost.NewPassword,
            confirmPassword = B17ProfileHost.NewPassword,
        });

        // then №2: 204 — дословное сравнение прошло, пароль сменён.
        Assert.True(
            verbatimResponse.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204 для дословного currentPassword, фактически {(int)verbatimResponse.StatusCode}: {await verbatimResponse.Content.ReadAsStringAsync()}");

        // then №2: вход по 'NewPass1!' после контрольного PUT — 200
        // (новый пароль действительно применён).
        using var loginClient = B17ProfileHost.Create(_factory);
        using var newPasswordLogin = await B17ProfileHost.LoginAsync(
            loginClient, Login, B17ProfileHost.NewPassword);
        Assert.True(
            newPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 входа по новому паролю после контрольного PUT, фактически {(int)newPasswordLogin.StatusCode}: {await newPasswordLogin.Content.ReadAsStringAsync()}");
    }
}
