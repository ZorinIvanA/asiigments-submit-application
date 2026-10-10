using LabsApp.IntegrationTests.B09.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

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
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-08/Auth (староволновые Ts189/Ts190 в B-21/B-22/B-15/B-17
/// помечены арбитражем дубликатами). Поведенческая часть кейса исполнима
/// дословно и исполнена в собственной зоне батча B-09 (прецедент c-1052);
/// расхождение размещения зафиксировано в scenario_change_requests.
///
/// Состав секретов: три пароля потока, 6-значный код (извлечён из записей
/// 'EmailDev'), resetToken из ответа recovery/confirm, значения ВСЕХ выданных
/// cookie, хэш пароля из хранилища и SHA-256-хэши значений refresh-cookie
/// («хэши» методики NFR-006). Сопоставление — по сериализованной форме записей
/// (категория + шаблон + сообщение + состояние).
/// </summary>
public sealed class Ts151_Nfr006FullFlowSecretsNotLoggedTests
{
    private const string LoginName = "b09flow";
    private const string Email = "b09flow@example.com";
    private const string FullName = "Флоу Секретов Девять";
    private const string RegisterPassword = "Register0rd!";
    private const string ResetPassword = "Reset0rd1!";
    private const string ChangePassword = "Change0rd2!";

    [Fact]
    public async Task FullAuthFlow_NoSecretsOutsideEmailDevCategory()
    {
        using var factory = new B09AuthDevFactory();

        // given: Development-хост с log-sink; сценарий видит только записи потока.
        factory.LogSink.Clear();
        using var client = B09AuthSupport.CreateClient(factory);
        var secrets = new List<string>();

        // when (1): register → 201; секрет — пароль регистрации + выпущенные cookie.
        using (var register = await B09AuthHttp.PostJsonAsync(client, B09AuthSupport.RegisterEndpoint,
                   "{\"fullName\":\"" + FullName + "\",\"login\":\"" + LoginName + "\",\"email\":\"" + Email + "\"," +
                   "\"password\":\"" + RegisterPassword + "\",\"repeatPassword\":\"" + RegisterPassword + "\"}"))
        {
            B09AuthSupport.AssertStatus(register, HttpStatusCode.Created, "register (TS-151, шаг 1)");
            CarryCookies(client, register, secrets);
        }

        secrets.Add(RegisterPassword);

        // when (2): login с паролем регистрации → 200.
        using (var firstLogin = await B09AuthHttp.PostJsonAsync(client, B09AuthSupport.LoginEndpoint,
                   "{\"login\":\"" + LoginName + "\",\"password\":\"" + RegisterPassword + "\"}"))
        {
            B09AuthSupport.AssertStatus(firstLogin, HttpStatusCode.OK, "login с паролем регистрации (TS-151, шаг 2)");
            CarryCookies(client, firstLogin, secrets);
        }

        // when (3): recovery/request → 200 с пустым телом; код восстановления
        // тесту известен ТОЛЬКО из категории 'EmailDev'.
        using (var recoveryRequest = await B09AuthHttp.PostJsonAsync(client, B09AuthSupport.RecoveryRequestEndpoint,
                   "{\"email\":\"" + Email + "\"}"))
        {
            B09AuthSupport.AssertStatus(recoveryRequest, HttpStatusCode.OK, "recovery/request (TS-151, шаг 3)");
            Assert.True(
                string.IsNullOrEmpty(await recoveryRequest.Content.ReadAsStringAsync()),
                "recovery/request: ожидалось пустое тело ответа (ISS-014).");
        }

        var emailRecords = factory.LogSink.OfCategory(B09AuthSupport.EmailDevCategory);
        var codes = B09AuthSupport.ExtractSixDigitCodes(emailRecords);
        Assert.True(
            codes.Count > 0,
            "код восстановления не найден в категории 'EmailDev': поток не может продолжиться без секрета.");
        secrets.AddRange(codes);

        // when (4): recovery/confirm {email, code} → 200 {resetToken}.
        string resetToken;
        using (var confirm = await B09AuthHttp.PostJsonAsync(client, B09AuthSupport.RecoveryConfirmEndpoint,
                   "{\"email\":\"" + Email + "\",\"code\":\"" + codes[0] + "\"}"))
        {
            B09AuthSupport.AssertStatus(confirm, HttpStatusCode.OK, "recovery/confirm (TS-151, шаг 4)");
            var confirmBody = await B09AuthSupport.ReadJsonObjectAsync(confirm, "тело recovery/confirm (TS-151)");
            resetToken = B09AuthSupport.StringProperty(confirmBody, "resetToken")
                ?? throw new Xunit.Sdk.XunitException("recovery/confirm: в ответе нет строки resetToken.");
        }

        secrets.Add(resetToken);

        // when (5): reset-password {resetToken, password, confirmPassword} → 204;
        // секрет — пароль сброса.
        using (var reset = await B09AuthHttp.PostJsonAsync(client, B09AuthSupport.ResetPasswordEndpoint,
                   "{\"resetToken\":\"" + resetToken + "\",\"password\":\"" + ResetPassword + "\"," +
                   "\"confirmPassword\":\"" + ResetPassword + "\"}"))
        {
            B09AuthSupport.AssertStatus(reset, HttpStatusCode.NoContent, "reset-password (TS-151, шаг 5)");
        }

        secrets.Add(ResetPassword);

        // when (6): login с паролем сброса → 200.
        using (var secondLogin = await B09AuthHttp.PostJsonAsync(client, B09AuthSupport.LoginEndpoint,
                   "{\"login\":\"" + LoginName + "\",\"password\":\"" + ResetPassword + "\"}"))
        {
            B09AuthSupport.AssertStatus(secondLogin, HttpStatusCode.OK, "login с паролем сброса (TS-151, шаг 6)");
            CarryCookies(client, secondLogin, secrets);
        }

        // when (7): me/password {currentPassword, password, confirmPassword} → 204;
        // секрет — пароль смены (запрос аутентифицирован access-cookie шага 6).
        using (var change = await client.PutAsync(
                   B09AuthSupport.MePasswordEndpoint,
                   new StringContent(
                       "{\"currentPassword\":\"" + ResetPassword + "\",\"password\":\"" + ChangePassword + "\"," +
                       "\"confirmPassword\":\"" + ChangePassword + "\"}",
                       Encoding.UTF8,
                       "application/json")))
        {
            B09AuthSupport.AssertStatus(change, HttpStatusCode.NoContent, "me/password (TS-151, шаг 7)");
        }

        secrets.Add(ChangePassword);

        // when (8): logout → 204 (с cookie сессии — отзыв живого refresh).
        using (var logout = await B09AuthHttp.PostNoBodyAsync(client, B09AuthSupport.LogoutEndpoint))
        {
            B09AuthSupport.AssertStatus(logout, HttpStatusCode.NoContent, "logout (TS-151, шаг 8)");
        }

        // given (хэши): хэш пароля из хранилища и SHA-256-хэши значений
        // refresh-cookie (значения/хэши токенов и хэши паролей — тоже секреты).
        var storedHash = factory.Services
            .GetRequiredService<IUserRepository>()
            .GetByLogin(LoginName)!.PasswordHash;
        secrets.Add(storedHash);
        secrets.AddRange(secrets
            .Where(value => value.Length == 43 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            .Select(B09AuthSupport.Sha256Hex)
            .ToList());

        // when: сопоставление всех записей sink с секретами.
        var comparableSecrets = secrets
            .Where(secret => !string.IsNullOrEmpty(secret) && secret.Length >= 6)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var violating = factory.LogSink.Snapshot()
            .Where(record => !string.Equals(record.Category, B09AuthSupport.EmailDevCategory, StringComparison.Ordinal))
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
            Assert.Equal(B09AuthSupport.EmailDevCategory, record.Category);
            Assert.Contains(B09AuthSupport.EmailDevMarker, record.Serialize(), StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Переносит cookie ответа в заголовок Cookie клиента (ручной контейнер:
    /// клиент зоны без HandleCookies) и добавляет значения в набор секретов
    /// (значения cookie — секреты NFR-006).
    /// </summary>
    private static void CarryCookies(HttpClient client, HttpResponseMessage response, List<string> secrets)
    {
        var cookies = B09AuthSupport.ParseSetCookie(response);
        Assert.True(
            cookies.Count == 2,
            $"TS-151: ожидалось два Set-Cookie (access_token и refresh_token), получено {cookies.Count}.");
        Assert.Equal("access_token", cookies[0].Name);
        Assert.Equal("refresh_token", cookies[1].Name);

        B09AuthHttp.SetRequestCookies(client, (cookies[0].Name, cookies[0].Value), (cookies[1].Name, cookies[1].Value));
        foreach (var cookie in cookies)
        {
            if (!string.IsNullOrEmpty(cookie.Value))
            {
                secrets.Add(cookie.Value);
            }
        }
    }
}
