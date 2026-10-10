using LabsApp.Auth;

namespace LabsApp.IntegrationTests.B11.Infrastructure;

/// <summary>
/// HTTP-помощники recovery/reset-кейсов батча B-11 (IF-008): пути эндпойнтов
/// восстановления, POST-отправка JSON-тел и точные проверки формы «пустое тело»
/// (ISS-014: recovery/request — 200 с Content-Length: 0, НЕ '{}'; FR-014/ISS-004:
/// reset-password — 204 с пустым телом).
/// </summary>
public static class B11RecoveryApi
{
    /// <summary>POST /api/v1/auth/recovery/request (FR-012).</summary>
    public const string RequestPath = "/api/v1/auth/recovery/request";

    /// <summary>POST /api/v1/auth/recovery/confirm (FR-013).</summary>
    public const string ConfirmPath = "/api/v1/auth/recovery/confirm";

    /// <summary>POST /api/v1/auth/reset-password (FR-014).</summary>
    public const string ResetPasswordPath = "/api/v1/auth/reset-password";

    /// <summary>POST /auth/recovery/request {email}.</summary>
    public static Task<HttpResponseMessage> RecoveryRequestAsync(HttpClient client, string email) =>
        HostClients.PostJsonAsync(client, RequestPath, System.Text.Json.JsonSerializer.Serialize(new { email }));

    /// <summary>POST /auth/recovery/confirm {email, code}.</summary>
    public static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string email, string code) =>
        HostClients.PostJsonAsync(client, ConfirmPath, System.Text.Json.JsonSerializer.Serialize(new { email, code }));

    /// <summary>POST /auth/reset-password {resetToken, password, confirmPassword}.</summary>
    public static Task<HttpResponseMessage> ResetPasswordAsync(
        HttpClient client,
        string resetToken,
        string password,
        string confirmPassword) =>
        HostClients.PostJsonAsync(
            client,
            ResetPasswordPath,
            System.Text.Json.JsonSerializer.Serialize(new { resetToken, password, confirmPassword }));

    /// <summary>POST /auth/refresh с единственным cookie refresh_token=value (IF-007).</summary>
    public static Task<HttpResponseMessage> RefreshWithCookieAsync(HttpClient client, string refreshValue)
    {
        HostClients.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshValue);
        return HostClients.PostWithoutBodyAsync(client, HostClients.RefreshPath);
    }

    /// <summary>
    /// then-проверка recovery/request (ISS-014): 200; Content-Length: 0; тело ровно
    /// 0 байт (НЕ JSON-объект '{}').
    /// </summary>
    public static async Task AssertEmptyBodyOkAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Content is not null,
            "Ответ 200 без content: заголовок Content-Length: 0 не выставлен (ISS-014: пустое тело 0 байт, НЕ '{}').");
        Assert.True(
            response.Content.Headers.ContentLength == 0,
            $"Ожидался Content-Length: 0, фактически " +
            $"{response.Content.Headers.ContentLength?.ToString() ?? "<отсутствует>"} (ISS-014).");
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>then-проверка успешного reset-password (FR-014/ISS-004): 204 с пустым телом.</summary>
    public static async Task AssertNoContentAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        if (response.Content is not null)
        {
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }
    }
}
