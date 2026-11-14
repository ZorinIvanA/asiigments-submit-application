using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-179 «reset-password: пароль длины 8, нарушающий ровно одно композиционное
/// правило — 400, токен не гасится» (boundary, FR-014/FR-006, P1; СКЕП2-ISS-001).
///
/// given: студент student01 создан DI-сидом (старый пароль известен); живой
///        resetToken получен через recovery/request → recovery/confirm (код из
///        [DEV-EMAIL]-записи категории 'EmailDev' тестового sink, IF-005);
///        счётчик KDF «сброшен» — измерение дельтами снимков IKdfCounter (IF-002);
/// when:  три POST /api/v1/auth/reset-password с ОДНИМ и тем же живым токеном и
///        password 'Abcdefg!' (нет цифры) / '1234567!' (нет буквы) /
///        'Abcdefg1' (нет спецзнака) — в каждом confirmPassword дословно равен
///        password; затем контрольный повтор тем же токеном с валидными
///        password 'NewPass1!' и confirmPassword 'NewPass1!';
/// then:  первые три — 400 'Данные заполнены неверно'; errors.password — ровно
///        ОДИН текст нарушенного правила (дословно из словаря ErrorTexts, FR-023;
///        текста password.min нет — длина 8 валидна); токен НЕ погашен ни одним
///        из трёх отказов — контрольный повтор с валидным паролем даёт 204
///        (FR-014: «при полевых ошибках 400 VALIDATION и токен НЕ гасится»);
///        Δkdf=0 на каждый из трёх негативных запросов (хэширование — только при
///        успехе, FR-004); после контрольного 204 вход старым паролем — 401,
///        новым — 200.
///
/// Примечание зоны: зона-владелец кейса — test_zone батча-получателя, в текущей
/// волне tests/integration/B-14 (переиздание a-132: прежнее закрепление файла
/// за B-11 снято, файл Ts179 в B-11 не создаёт никто; зона создания канонического
/// файла с текущим номером = test_zone батча-получателя).
/// </summary>
public sealed class Ts179_ResetPasswordCompositionAtMinLengthTests : IClassFixture<B14RecoveryWebAppFactory>
{
    /// <summary>Тексты словаря «Текстов ошибок полей» (ErrorTexts, FR-023) — дословно.</summary>
    private const string ExpectedDigitText = ErrorTexts.PasswordDigit;

    private const string ExpectedLetterText = ErrorTexts.PasswordLetter;
    private const string ExpectedSpecialText = ErrorTexts.PasswordSpecial;

    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Студент Первый";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts179_ResetPasswordCompositionAtMinLengthTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ResetPassword_Length8WithSingleBrokenRule_SingleText_TokenKept_ValidRetryResets()
    {
        // given: студент DI-сидом (старый пароль известен); живой resetToken;
        // счётчик KDF — измерение дельтами снимков (IF-002).
        _ = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var client = B14Harness.Create(_factory);
        var code = await B14RecoveryHarness.RequestLiveCodeAsync(_factory, client, Email);
        using var confirm = await B14RecoveryHarness.ConfirmAsync(client, Email, code);
        Assert.True(
            confirm.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: recovery/confirm живым кодом → 200, фактически " +
            $"{(int)confirm.StatusCode}: {await confirm.Content.ReadAsStringAsync()}");
        var confirmBody = await B14Assertions.ReadRootObjectAsync(confirm);
        Assert.True(
            confirmBody.TryGetProperty("resetToken", out var resetTokenProperty)
            && resetTokenProperty.ValueKind == System.Text.Json.JsonValueKind.String
            && !string.IsNullOrWhiteSpace(resetTokenProperty.GetString()),
            "Предусловие кейса: confirm обязан выдать непустой resetToken.");
        var resetToken = resetTokenProperty.GetString()!;

        // when (1): password 'Abcdefg!' — длина ровно 8, есть буквы и спецзнак
        // '!', НЕТ цифры; confirmPassword дословно равен password.
        Assert.Equal(8, "Abcdefg!".Length);
        var kdfBeforeFirst = B14KdfProbe.Snapshot(_factory.Services);
        using var noDigit = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, "Abcdefg!", "Abcdefg!");

        // then (1): 400 'Данные заполнены неверно'; errors.password — ровно один
        // текст digit (дословно); текста password.min нет — длина 8 валидна;
        // Δkdf=0 (хэширование — только при успехе, FR-004).
        await AssertRejectedWithSinglePasswordErrorAsync(noDigit, ExpectedDigitText);
        AssertKdfUnchanged(kdfBeforeFirst, "noDigit");

        // when (2): password '1234567!' — длина ровно 8, есть цифры и '!',
        // НЕТ буквы; тот же живой токен.
        Assert.Equal(8, "1234567!".Length);
        var kdfBeforeSecond = B14KdfProbe.Snapshot(_factory.Services);
        using var noLetter = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, "1234567!", "1234567!");

        // then (2): 400; errors.password — ровно ['Пароль должен содержать хотя
        // бы одну букву']; Δkdf=0.
        await AssertRejectedWithSinglePasswordErrorAsync(noLetter, ExpectedLetterText);
        AssertKdfUnchanged(kdfBeforeSecond, "noLetter");

        // when (3): password 'Abcdefg1' — длина ровно 8, есть буквы и цифра,
        // НЕТ спецзнака; тот же живой токен.
        Assert.Equal(8, "Abcdefg1".Length);
        var kdfBeforeThird = B14KdfProbe.Snapshot(_factory.Services);
        using var noSpecial = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, "Abcdefg1", "Abcdefg1");

        // then (3): 400; errors.password — ровно ['Пароль должен содержать хотя
        // бы один специальный знак']; Δkdf=0.
        await AssertRejectedWithSinglePasswordErrorAsync(noSpecial, ExpectedSpecialText);
        AssertKdfUnchanged(kdfBeforeThird, "noSpecial");

        // then: токен НЕ погашен ни одним из трёх отказов — контрольный повтор
        // тем же токеном с валидными совпадающими паролями даёт 204 (FR-014).
        using var retry = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, B14Harness.NewPassword, B14Harness.NewPassword);
        Assert.True(
            retry.StatusCode == HttpStatusCode.NoContent,
            $"Токен обязан остаться живым после трёх полевых ошибок: ожидался 204, " +
            $"фактически {(int)retry.StatusCode}: {await retry.Content.ReadAsStringAsync()}");

        // then: после контрольного 204 вход старым паролем — 401, новым — 200.
        using var oldPasswordLogin = await B14Harness.LoginAsync(
            B14Harness.Create(_factory), Login, B14Harness.TestUserPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался 401 на вход старым паролем, фактически " +
            $"{(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");
        using var newPasswordLogin = await B14Harness.LoginAsync(
            B14Harness.Create(_factory), Login, B14Harness.NewPassword);
        Assert.True(
            newPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался 200 на вход новым паролем, фактически " +
            $"{(int)newPasswordLogin.StatusCode}: {await newPasswordLogin.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// Общие then-шаги одного негативного запроса: 400 'Данные заполнены
    /// неверно'; errors.password — ровно ОДИН текст нарушенного правила
    /// (дословно из словаря); текста password.min в errors.password нет —
    /// длина 8 включительно валидна.
    /// </summary>
    private static async Task AssertRejectedWithSinglePasswordErrorAsync(
        HttpResponseMessage response,
        string expectedPasswordError)
    {
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 на пароль длины 8 с одним нарушенным правилом, фактически " +
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var envelope = await B14Assertions.ReadRootObjectAsync(response);
        B14Assertions.MessageIs(envelope, B14RecoveryHarness.InvalidDataMessage);
        B14Assertions.ErrorFieldEquals(envelope, "password", expectedPasswordError);
    }

    /// <summary>Δkdf=0 по всем меткам между базовым снимком и текущим (FR-004).</summary>
    private void AssertKdfUnchanged(IReadOnlyDictionary<string, long> before, string step)
    {
        var after = B14KdfProbe.Snapshot(_factory.Services);
        Assert.True(
            B14KdfProbe.TotalDelta(before, after) == 0,
            $"Ожидался Δkdf=0 на негативном запросе ({step}), фактически " +
            $"{B14KdfProbe.TotalDelta(before, after)}.");
    }
}
