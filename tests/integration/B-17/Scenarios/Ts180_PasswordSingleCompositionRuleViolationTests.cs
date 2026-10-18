using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;
using System.Text.Json;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-180 «me/password: новый пароль длины 8, нарушающий ровно одно
/// композиционное правило — 400» (boundary, FR-016 + FR-006, P1;
/// СКЕП2-ISS-001: зона 8–128 с одним нарушенным правилом не стимулировалась
/// прежним кейсом TS-087 со стимулом 'abc' длины 3).
///
/// given: пользователь авторизован (сессия — минтованный access-cookie,
///        ADR-015); currentPassword верен (текущий пароль известен); счётчик
///        KDF сбрасывается перед каждым запросом (дельты снимков
///        IKdfCounter.Snapshot()).
/// when:  три PUT /api/v1/me/password с currentPassword=верный и паролями
///        'Abcdefg!' (нет цифры) / '1234567!' (нет буквы) / 'Abcdefg1' (нет
///        спецзнака) — каждый длиной РОВНО 8 (правило min валидно),
///        confirmPassword дословно равен password; затем контрольный вход
///        старым паролем.
/// then:  все три — 400 'Данные заполнены неверно'; errors.password — ровно
///        один текст нарушенного правила (словарь FR-006; текста min нет —
///        длина 8 валидна); errors.confirmPassword отсутствует (повторы
///        совпадают); пароль НЕ изменён — контрольный вход старым паролем —
///        200; Δkdf=1 на каждый запрос (ровно одна деривация — verify
///        currentPassword; хэширование нового не выполняется — паритет
///        арифметики с TS-171).
/// </summary>
public sealed class Ts180_PasswordSingleCompositionRuleViolationTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts180";
    private const string Email = "ts180@x.ru";

    /// <summary>
    /// Три стимула when кейса (длина каждого — ровно 8) с ожидаемым
    /// ЕДИНСТВЕННЫМ текстом нарушенного композиционного правила.
    /// </summary>
    private static readonly (string Password, string ExpectedError)[] SingleRuleViolations =
    {
        ("Abcdefg!", ErrorTexts.PasswordDigit),
        ("1234567!", ErrorTexts.PasswordLetter),
        ("Abcdefg1", ErrorTexts.PasswordSpecial),
    };

    private readonly B17WebAppFactory _factory;

    public Ts180_PasswordSingleCompositionRuleViolationTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_Length8_ViolatingExactlyOneCompositionRule_Returns400WithSingleError_OneKdf_PasswordKept()
    {
        // given: пользователь; currentPassword верен (текущий пароль известен).
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-180",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        foreach (var (weakPassword, expectedError) in SingleRuleViolations)
        {
            // given: «счётчик KDF сбрасывается перед запросом» — база Δkdf.
            var before = B17ProfileHost.KdfSnapshot(_factory);

            // when: PUT с паролем длины 8, нарушающим ровно одно правило;
            // confirmPassword дословно равен password.
            using var response = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
            {
                currentPassword = B17ProfileHost.TestUserPassword,
                password = weakPassword,
                confirmPassword = weakPassword,
            });

            // then: 400 «Данные заполнены неверно».
            Assert.True(
                response.StatusCode == HttpStatusCode.BadRequest,
                $"пароль «{weakPassword}»: ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            var root = await B17BodyAssertions.ReadRootObjectAsync(response);
            B17BodyAssertions.MessageIs(root, ErrorTexts.InvalidData);

            // then: errors.password — РОВНО один текст нарушенного правила
            // (текста min нет — длина 8 валидна; прочие правила состава
            // соблюдены самим стимулом).
            B17BodyAssertions.ErrorFieldEquals(root, "password", expectedError);

            // then: errors.confirmPassword отсутствует — повторы совпадают.
            Assert.True(
                root.TryGetProperty("errors", out var errors),
                $"пароль «{weakPassword}»: в теле 400 отсутствует ключ errors.");
            Assert.Equal(JsonValueKind.Object, errors.ValueKind);
            Assert.False(
                errors.TryGetProperty("confirmPassword", out var confirmPasswordErrors),
                $"пароль «{weakPassword}»: ожидалось отсутствие errors.confirmPassword, фактически: {confirmPasswordErrors.ToString()}.");

            // then: Δkdf=1 — ровно одна деривация (verify currentPassword);
            // хэширование нового не выполнялось.
            var after = B17ProfileHost.KdfSnapshot(_factory);
            var delta = B17ProfileHost.KdfTotalDelta(before, after);
            Assert.True(
                delta == 1,
                $"пароль «{weakPassword}»: ожидался Δkdf=1 (только verify currentPassword), фактически {delta}.");
        }

        // then: пароль НЕ изменён после всех трёх запросов — контрольный вход
        // старым паролем даёт 200.
        using var loginClient = B17ProfileHost.Create(_factory);
        using var oldPasswordLogin = await B17ProfileHost.LoginAsync(
            loginClient, Login, B17ProfileHost.TestUserPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 контрольного входа старым паролем, фактически {(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");
    }
}
