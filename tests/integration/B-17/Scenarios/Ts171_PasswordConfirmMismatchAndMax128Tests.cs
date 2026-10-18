using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-171 «me/password: несовпадение confirmPassword и пароль 129 — пакет
/// ошибок по полям» (boundary, FR-016 + FR-006, P1; ревью R4c).
///
/// given: пользователь авторизован; currentPassword верен (текущий пароль
///        известен); счётчик KDF сбрасывается перед каждым запросом (дельты
///        снимков IKdfCounter.Snapshot()); старый пароль для контроля входа
///        известен.
/// when:  PUT /api/v1/me/password {currentPassword: верный, password:'NewPass1!',
///        confirmPassword:'Other2!'} (несовпадение); отдельно PUT
///        {currentPassword: верный, password: строка 129 символов валидного
///        состава, confirmPassword: та же строка 129} (верхняя граница длины).
/// then:  первый — 400 'Данные заполнены неверно',
///        errors.confirmPassword=['Пароли не совпадают'] (дословный текст
///        словаря password.mismatch; ключ — по имени поля confirmPassword, FR-016
///        п.2 «валидация password/confirmPassword по FR-006 → 400 VALIDATION
///        errors {password, confirmPassword}»); второй — 400,
///        errors.password=['Пароль — не более 128 символов'] (единственная
///        ошибка пароля при длине &gt;128 — ISS-016/ASM-017); оба запроса: пароль
///        НЕ изменён (вход старым паролем — 200), Δkdf=1 на каждый запрос
///        (ровно одна деривация — verify currentPassword; хэширование нового
///        не выполняется).
/// </summary>
public sealed class Ts171_PasswordConfirmMismatchAndMax128Tests : IClassFixture<B17WebAppFactory>
{
    private const string MismatchLogin = "ts171m";
    private const string MismatchEmail = "ts171m@x.ru";
    private const string OversizedLogin = "ts171x";
    private const string OversizedEmail = "ts171x@x.ru";

    /// <summary>Новый пароль длиной 129 валидного состава: буквы, цифра, спецзнак.</summary>
    private static string OversizedPassword129 => new string('a', 127) + "1!";

    private readonly B17WebAppFactory _factory;

    public Ts171_PasswordConfirmMismatchAndMax128Tests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithMismatchedConfirm_Returns400WithConfirmError_OneKdf_PasswordKept()
    {
        // given: пользователь; currentPassword верен (текущий пароль известен).
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: MismatchLogin,
            email: MismatchEmail,
            fullName: "Студент ТС-171 Мисматч",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // given: «счётчик KDF сбрасывается перед запросом» — база Δkdf.
        var before = B17ProfileHost.KdfSnapshot(_factory);

        // when: PUT с несовпадающим confirmPassword ('Other2!' — дословно кейса).
        using var response = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
        {
            currentPassword = B17ProfileHost.TestUserPassword,
            password = B17ProfileHost.NewPassword,
            confirmPassword = "Other2!",
        });

        // then: 400 «Данные заполнены неверно»; errors.confirmPassword дословно.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.InvalidData);
        B17BodyAssertions.ErrorFieldEquals(root, "confirmPassword", ErrorTexts.PasswordMismatch);

        // then: Δkdf=1 — ровно одна деривация (verify currentPassword);
        // хэширование нового не выполнялось.
        var after = B17ProfileHost.KdfSnapshot(_factory);
        var delta = B17ProfileHost.KdfTotalDelta(before, after);
        Assert.True(
            delta == 1,
            $"Ожидался Δkdf=1 (только verify currentPassword), фактически {delta}.");

        // then: пароль НЕ изменён — вход старым паролем даёт 200.
        using var loginClient = B17ProfileHost.Create(_factory);
        using var oldPasswordLogin = await B17ProfileHost.LoginAsync(
            loginClient, MismatchLogin, B17ProfileHost.TestUserPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 входа старым паролем (пароль не изменён), фактически {(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task PutPassword_With129CharValidComposition_Returns400WithSingleMaxError_OneKdf_PasswordKept()
    {
        // given: пользователь; currentPassword верен.
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: OversizedLogin,
            email: OversizedEmail,
            fullName: "Студент ТС-171 Граница",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        var oversized = OversizedPassword129;
        Assert.Equal(129, oversized.Length);

        // given: «счётчик KDF сбрасывается перед запросом» — база Δkdf.
        var before = B17ProfileHost.KdfSnapshot(_factory);

        // when: PUT с паролем-повтором длиной 129 (верхняя граница превышена).
        using var response = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
        {
            currentPassword = B17ProfileHost.TestUserPassword,
            password = oversized,
            confirmPassword = oversized,
        });

        // then: 400; errors.password = ['Пароль — не более 128 символов'] —
        // ЕДИНСТВЕННАЯ ошибка пароля при длине >128 (ISS-016/ASM-017:
        // прочие правила при длине >128 не проверяются).
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.InvalidData);
        B17BodyAssertions.ErrorFieldEquals(root, "password", ErrorTexts.PasswordMax);

        // then: Δkdf=1 — ровно одна деривация (verify currentPassword);
        // хэширование нового не выполнялось.
        var after = B17ProfileHost.KdfSnapshot(_factory);
        var delta = B17ProfileHost.KdfTotalDelta(before, after);
        Assert.True(
            delta == 1,
            $"Ожидался Δkdf=1 (только verify currentPassword), фактически {delta}.");

        // then: пароль НЕ изменён — вход старым паролем даёт 200.
        using var loginClient = B17ProfileHost.Create(_factory);
        using var oldPasswordLogin = await B17ProfileHost.LoginAsync(
            loginClient, OversizedLogin, B17ProfileHost.TestUserPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 входа старым паролем (пароль не изменён), фактически {(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");
    }
}
