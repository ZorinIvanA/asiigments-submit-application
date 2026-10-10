using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-075 (P0, nfr; NFR-006/FR-012) «NFR-006 (Development): полный auth-поток
/// не оставляет секреты вне 'EmailDev'».
/// given: Development; кастомный log-sink, разделяющий категории; зафиксированы
///        тестом: пароль регистрации, код восстановления, resetToken, значения
///        cookie.
/// when:  полный поток: register → login → recovery/request → recovery/confirm →
///        reset-password → logout; после — сканирование всех записей sink вне
///        категории 'EmailDev'.
/// then:  ни одна запись вне 'EmailDev' не содержит ни один из секретов (пароль,
///        код, resetToken, значения cookie); код присутствует только в
///        [DEV-EMAIL]-записи (NFR-006 verification (а)).
/// </summary>
public sealed class Ts075_Nfr006DevFullFlowSecretsNotLoggedTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string RegisterEndpoint = "/api/v1/auth/register";
    private const string LogoutEndpoint = "/api/v1/auth/logout";
    private const string LoginName = "ts075flow";
    private const string Email = "ts075flow@example.com";
    private const string FullName = "Поток Полноты Пятнадцать";
    private const string RegisterPassword = "Register0rd!";
    private const string ResetPassword = "Reset0rd1!";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS075_FullAuthFlow_NoSecretsOutsideEmailDevCategory()
    {
        // given: Development-хост с log-sink; клиент без авто-cookie (значения
        // cookie переносятся вручную и фиксируются как секреты).
        using var client = B13RecoveryHarness.CreateClient(_factory);
        var jar = new Dictionary<string, string>(StringComparer.Ordinal);
        var secrets = new List<string> { RegisterPassword, ResetPassword };

        // when (1): register → 201; секреты — пароль регистрации, значения cookie.
        using (var register = await client.PostAsJsonAsync(RegisterEndpoint, new
        {
            fullName = FullName,
            login = LoginName,
            email = Email,
            password = RegisterPassword,
            repeatPassword = RegisterPassword,
        }))
        {
            Assert.True(
                register.StatusCode == HttpStatusCode.Created,
                $"Шаг 1 register: ожидался 201, фактически {(int)register.StatusCode}: " +
                $"{await register.Content.ReadAsStringAsync()}");
            CarryCookies(client, register, jar, secrets);
        }

        // when (2): login → 200; секреты — значения cookie сессии.
        using (var login = await B13RecoveryHarness.LoginAsync(client, LoginName, RegisterPassword))
        {
            Assert.True(
                login.StatusCode == HttpStatusCode.OK,
                $"Шаг 2 login: ожидался 200, фактически {(int)login.StatusCode}.");
            CarryCookies(client, login, jar, secrets);
        }

        // when (3): recovery/request → 200 с пустым телом; код восстановления
        // тесту известен ТОЛЬКО из категории 'EmailDev' (IF-005).
        using (var request = await B13RecoveryApi.RecoveryRequestAsync(client, Email))
        {
            await B13RecoveryApi.AssertEmptyBodyOkAsync(request);
        }

        var code = _factory.LogSink.GetLastRecoveryCodeForEmail(Email);
        secrets.Add(code);

        // when (4): recovery/confirm {email, code} → 200 {resetToken}.
        string? resetToken;
        using (var confirm = await B13RecoveryApi.ConfirmAsync(client, Email, code))
        {
            var body = await ApiAssert.ReadOkJsonAsync(confirm);
            resetToken = body.GetProperty("resetToken").GetString();
            Assert.False(
                string.IsNullOrWhiteSpace(resetToken),
                "Шаг 4 recovery/confirm: в ответе нет непустого resetToken.");
        }

        secrets.Add(resetToken!);

        // when (5): reset-password {resetToken, password, confirmPassword} → 204.
        using (var reset = await B13RecoveryApi.ResetPasswordAsync(
                   client, resetToken!, ResetPassword, ResetPassword))
        {
            await B13RecoveryApi.AssertNoContentAsync(reset);
        }

        // when (6): logout → 204 (идемпотентный выход с cookie сессии).
        using (var logout = await client.PostAsync(LogoutEndpoint, content: null))
        {
            Assert.True(
                logout.StatusCode == HttpStatusCode.NoContent,
                $"Шаг 6 logout: ожидался 204, фактически {(int)logout.StatusCode}.");
            CarryCookies(client, logout, jar, secrets);
        }

        // when: сканирование всех записей sink вне категории 'EmailDev'.
        var comparableSecrets = secrets
            .Where(secret => !string.IsNullOrEmpty(secret) && secret.Length >= 6)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var violating = _factory.LogSink.Snapshot()
            .Where(record => record.Category != B13RecoveryLogAsserts.EmailDevCategory)
            .Where(record => comparableSecrets.Any(secret => record.Message.Contains(secret, StringComparison.Ordinal)))
            .ToArray();

        // then: ни одна запись вне 'EmailDev' не содержит ни один из секретов
        // (пароль, код, resetToken, значения cookie).
        Assert.True(
            violating.Length == 0,
            "NFR-006: записи вне 'EmailDev' содержат секреты: " + string.Join(
                " | ",
                violating.Take(5).Select(record => $"[{record.Category}] {record.Message}")));

        // then: код присутствует только в [DEV-EMAIL]-записи.
        var recordsWithCode = _factory.LogSink.Snapshot()
            .Where(record => record.Message.Contains(code, StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(recordsWithCode);
        Assert.All(recordsWithCode, record =>
        {
            Assert.Equal(B13RecoveryLogAsserts.EmailDevCategory, record.Category);
            Assert.Contains(B13RecoveryLogAsserts.DevEmailMarker, record.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Переносит Set-Cookie ответа в jar и заголовок Cookie клиента (клиент без
    /// HandleCookies); непустые значения добавляются в набор секретов NFR-006.
    /// </summary>
    private static void CarryCookies(
        HttpClient client,
        HttpResponseMessage response,
        Dictionary<string, string> jar,
        List<string> secrets)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return;
        }

        foreach (var setCookie in setCookies)
        {
            var pair = setCookie.Split(';', 2)[0];
            var separator = pair.IndexOf('=');
            Assert.True(
                separator > 0,
                $"Set-Cookie без пары имя=значение: «{setCookie}».");
            var name = pair[..separator].Trim();
            var value = pair[(separator + 1)..].Trim();
            jar[name] = value;
            if (value.Length > 0)
            {
                secrets.Add(value);
            }
        }

        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add(
            "Cookie",
            string.Join("; ", jar.Select(cookie => $"{cookie.Key}={cookie.Value}")));
    }
}
