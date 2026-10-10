using LabsApp.IntegrationTests.B08.Auth.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Auth.Scenarios;

/// <summary>
/// TS-151 «NFR-006: полный auth-поток не оставляет секретов вне 'EmailDev'»
/// (nfr, NFR-006 + FR-005 + FR-008 + FR-012 + FR-014, P0).
///
/// given: Development; тестовый log-sink с категориями; тесту известны все
///        отправленные секреты: пароль регистрации, пароли смены/сброса, код
///        восстановления (из 'EmailDev'), resetToken, значения cookie.
/// when:  полный поток: register → login → recovery/request →
///        recovery/confirm → reset-password → login → me/password → logout;
///        сопоставление всех записей sink с секретами.
/// then:  ни одна запись вне категории 'EmailDev' не содержит ни один из
///        секретов (пароль, код, resetToken, значения cookie, хэши); код
///        присутствует только в [DEV-EMAIL]-записи (NFR-006(a), методика
///        verification).
///
/// Состав секретов: три пароля потока, 6-значный код (извлечён из записей
/// 'EmailDev'), resetToken из ответа recovery/confirm, значения ВСЕХ выданных
/// cookie, хэш пароля из хранилища и SHA-256-хэши значений refresh-cookie
/// («хэши» методики NFR-006). Сопоставление — по сериализованной форме записей
/// (категория + шаблон + сообщение + состояние).
/// </summary>
public sealed class Ts151_Nfr006FullFlowSecretsNotLoggedTests
{
    private const string LoginName = "b08flow";
    private const string Email = "b08flow@example.com";
    private const string FullName = "Флоу Секретов";
    private const string RegisterPassword = "Register0rd!";
    private const string ResetPassword = "Reset0rd1!";
    private const string ChangePassword = "Change0rd2!";

    [Fact]
    public async Task FullAuthFlow_NoSecretsOutsideEmailDevCategory()
    {
        using var factory = new B08AuthDevFactory();

        // given: Development-хост с log-sink; сценарий видит только записи потока.
        factory.LogSink.Clear();
        using var client = B08AuthHost.CreateClient(factory);

        var secrets = new List<string>();

        // when (1): register → 201; секрет — пароль регистрации + выпущенные cookie.
        using (var register = await client.PostAsync(
            B08AuthHost.RegisterEndpoint,
            "{\"fullName\":\"" + FullName + "\",\"login\":\"" + LoginName + "\",\"email\":\"" + Email + "\"," +
            "\"password\":\"" + RegisterPassword + "\",\"repeatPassword\":\"" + RegisterPassword + "\"}"))
        {
            B08AuthHost.AssertStatus(register, HttpStatusCode.Created, "register (TS-151, шаг 1)");
            CollectCookieSecrets(register, secrets);
        }

        secrets.Add(RegisterPassword);

        // when (2): login с паролем регистрации → 200.
        using (var firstLogin = await client.PostAsync(
            B08AuthHost.LoginEndpoint,
            "{\"login\":\"" + LoginName + "\",\"password\":\"" + RegisterPassword + "\"}"))
        {
            B08AuthHost.AssertStatus(firstLogin, HttpStatusCode.OK, "login с паролем регистрации (TS-151, шаг 2)");
            CollectCookieSecrets(firstLogin, secrets);
        }

        // when (3): recovery/request → 200 с пустым телом; код восстановления
        // тесту известен ТОЛЬКО из категории 'EmailDev'.
        using (var recoveryRequest = await client.PostAsync(
            B08AuthHost.RecoveryRequestEndpoint,
            "{\"email\":\"" + Email + "\"}"))
        {
            B08AuthHost.AssertStatus(recoveryRequest, HttpStatusCode.OK, "recovery/request (TS-151, шаг 3)");
            Assert.True(
                string.IsNullOrEmpty(await recoveryRequest.Content.ReadAsStringAsync()),
                "recovery/request: ожидалось пустое тело ответа (ISS-014).");
        }

        var emailRecords = factory.LogSink.OfCategory(B08AuthHost.EmailDevCategory);
        var codes = B08AuthHost.ExtractSixDigitCodes(emailRecords);
        Assert.True(
            codes.Count > 0,
            "код восстановления не найден в категории 'EmailDev': поток не может продолжиться без секрета.");
        secrets.AddRange(codes);

        // when (4): recovery/confirm {email, code} → 200 {resetToken}.
        string resetToken;
        using (var confirm = await client.PostAsync(
            B08AuthHost.RecoveryConfirmEndpoint,
            "{\"email\":\"" + Email + "\",\"code\":\"" + codes[0] + "\"}"))
        {
            B08AuthHost.AssertStatus(confirm, HttpStatusCode.OK, "recovery/confirm (TS-151, шаг 4)");
            var confirmBody = await B08AuthHost.ReadJsonObjectAsync(confirm, "тело recovery/confirm (TS-151)");
            resetToken = B08AuthHost.StringProperty(confirmBody, "resetToken")
                ?? throw new Xunit.Sdk.XunitException("recovery/confirm: в ответе нет строки resetToken.");
        }

        secrets.Add(resetToken);

        // when (5): reset-password {resetToken, password, confirmPassword} → 204;
        // секрет — пароль сброса.
        using (var reset = await client.PostAsync(
            B08AuthHost.ResetPasswordEndpoint,
            "{\"resetToken\":\"" + resetToken + "\",\"password\":\"" + ResetPassword + "\"," +
            "\"confirmPassword\":\"" + ResetPassword + "\"}"))
        {
            B08AuthHost.AssertStatus(reset, HttpStatusCode.NoContent, "reset-password (TS-151, шаг 5)");
        }

        secrets.Add(ResetPassword);

        // when (6): login с паролем сброса → 200.
        using (var secondLogin = await client.PostAsync(
            B08AuthHost.LoginEndpoint,
            "{\"login\":\"" + LoginName + "\",\"password\":\"" + ResetPassword + "\"}"))
        {
            B08AuthHost.AssertStatus(secondLogin, HttpStatusCode.OK, "login с паролем сброса (TS-151, шаг 6)");
            CollectCookieSecrets(secondLogin, secrets);
        }

        // when (7): me/password {currentPassword, password, confirmPassword} → 204;
        // секрет — пароль смены.
        using (var change = await client.PutAsync(
            B08AuthHost.MePasswordEndpoint,
            "{\"currentPassword\":\"" + ResetPassword + "\",\"password\":\"" + ChangePassword + "\"," +
            "\"confirmPassword\":\"" + ChangePassword + "\"}"))
        {
            B08AuthHost.AssertStatus(change, HttpStatusCode.NoContent, "me/password (TS-151, шаг 7)");
        }

        secrets.Add(ChangePassword);

        // when (8): logout → 204.
        using (var logout = await client.PostAsync(B08AuthHost.LogoutEndpoint, json: null))
        {
            B08AuthHost.AssertStatus(logout, HttpStatusCode.NoContent, "logout (TS-151, шаг 8)");
        }

        // given (хэши): хэш пароля из хранилища и SHA-256-хэши значений
        // refresh-cookie (значения/хэши токенов и хэши паролей — тоже секреты).
        var storedHash = factory.Services
            .GetRequiredService<LabsApp.Storage.IUserRepository>()
            .GetByLogin(LoginName)!.PasswordHash;
        secrets.Add(storedHash);
        secrets.AddRange(secrets
            .Where(value => value.Length == 43 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            .Select(B08AuthHost.Sha256Hex)
            .ToList());

        // when: сопоставление всех записей sink с секретами.
        var comparableSecrets = secrets
            .Where(secret => !string.IsNullOrEmpty(secret) && secret.Length >= 6)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var violating = factory.LogSink.Snapshot()
            .Where(record => !string.Equals(record.Category, B08AuthHost.EmailDevCategory, StringComparison.Ordinal))
            .Where(record => comparableSecrets.Any(secret => record.Serialize().Contains(secret, StringComparison.Ordinal)))
            .ToList();

        // then: ни одна запись вне категории 'EmailDev' не содержит ни один из секретов.
        Assert.True(
            violating.Count == 0,
            "NFR-006: записи вне 'EmailDev' содержат секреты: " + string.Join(
                " | ",
                violating.Take(5).Select(record => $"[{record.Category}] {record.Serialize()[..Math.Min(300, record.Serialize().Length)]}")));

        // then: код присутствует только в [DEV-EMAIL]-записи.
        var recordsWithCode = factory.LogSink.Snapshot()
            .Where(record => codes.Any(code => record.Serialize().Contains(code, StringComparison.Ordinal)))
            .ToList();
        Assert.All(recordsWithCode, record =>
        {
            Assert.Equal(B08AuthHost.EmailDevCategory, record.Category);
            Assert.Contains(B08AuthHost.EmailDevMarker, record.Serialize(), StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Добавляет в набор секретов значения всех Set-Cookie ответа (значения
    /// cookie — секреты NFR-006); refresh-значения (43 символа base64url) —
    /// кандидаты для добавления их SHA-256-хэшей в конце потока.
    /// </summary>
    private static void CollectCookieSecrets(HttpResponseMessage response, List<string> secrets)
    {
        foreach (var cookie in B08AuthHost.ParseSetCookie(response))
        {
            if (!string.IsNullOrEmpty(cookie.Value))
            {
                secrets.Add(cookie.Value);
            }
        }
    }
}
