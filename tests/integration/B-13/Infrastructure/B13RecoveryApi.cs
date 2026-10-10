using LabsApp.Auth;

namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>
/// HTTP-помощники recovery/reset-кейсов батча B-13 (IF-008): пути эндпойнтов
/// восстановления, POST-отправка JSON-тел и точные проверки формы «пустое тело»
/// (ISS-014: recovery/request — 200 с Content-Length: 0, НЕ '{}'; FR-014/ISS-004:
/// reset-password — 204 с пустым телом). Собственная копия механики канонической
/// зоны B-11 (чужие зоны недоступны для ссылок — BL-001 BUG-001).
/// </summary>
public static class B13RecoveryApi
{
    /// <summary>POST /api/v1/auth/recovery/request (FR-012).</summary>
    public const string RequestPath = "/api/v1/auth/recovery/request";

    /// <summary>POST /api/v1/auth/recovery/confirm (FR-013).</summary>
    public const string ConfirmPath = "/api/v1/auth/recovery/confirm";

    /// <summary>POST /api/v1/auth/reset-password (FR-014).</summary>
    public const string ResetPasswordPath = "/api/v1/auth/reset-password";

    /// <summary>POST /auth/recovery/request {email}.</summary>
    public static Task<HttpResponseMessage> RecoveryRequestAsync(HttpClient client, string email) =>
        PostJsonAsync(client, RequestPath, System.Text.Json.JsonSerializer.Serialize(new { email }));

    /// <summary>POST /auth/recovery/confirm {email, code}.</summary>
    public static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string email, string code) =>
        PostJsonAsync(client, ConfirmPath, System.Text.Json.JsonSerializer.Serialize(new { email, code }));

    /// <summary>POST /auth/reset-password {resetToken, password, confirmPassword}.</summary>
    public static Task<HttpResponseMessage> ResetPasswordAsync(
        HttpClient client,
        string resetToken,
        string password,
        string confirmPassword) =>
        PostJsonAsync(
            client,
            ResetPasswordPath,
            System.Text.Json.JsonSerializer.Serialize(new { resetToken, password, confirmPassword }));

    /// <summary>POST /auth/refresh с единственным cookie refresh_token=value (IF-007).</summary>
    public static Task<HttpResponseMessage> RefreshWithCookieAsync(HttpClient client, string refreshValue)
    {
        SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshValue);
        return PostWithoutBodyAsync(client, "/api/v1/auth/refresh");
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

    /// <summary>POST с произвольным JSON-телом.</summary>
    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, string json) =>
        client.PostAsync(path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    /// <summary>POST без тела — refresh идёт только по cookie (IF-007).</summary>
    private static Task<HttpResponseMessage> PostWithoutBodyAsync(HttpClient client, string path) =>
        client.PostAsync(path, content: null);

    /// <summary>Единственный заголовок Cookie клиента = name=value (клиент ведёт одну сессию кейса).</summary>
    private static void SetRequestCookie(HttpClient client, string name, string value)
    {
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"{name}={value}");
    }
}
