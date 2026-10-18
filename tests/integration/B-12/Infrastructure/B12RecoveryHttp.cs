using LabsApp.Auth;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B12.Infrastructure;

/// <summary>
/// HTTP-помощники recovery/reset-кейсов батча B-12 (IF-008; копия механики зоны
/// B-11 — чужие харнесы недоступны для ссылок, BL-001 BUG-001): клиент без
/// авто-редиректов и без cookie-контейнера, cookie-заголовки под тестом и точные
/// проверки формы «пустое тело» (ISS-014: recovery/request — 200 с
/// Content-Length: 0, НЕ '{}'; FR-014/ISS-004: reset-password — 204 с пустым
/// телом). Маршруты POST-хелперов — константы B12RecoveryHarness (единый источник
/// путей зоны).
/// </summary>
public static class B12RecoveryHttp
{
    /// <summary>Клиент без авто-редиректов (точные статусы) и без cookie-контейнера.</summary>
    public static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>Единственный заголовок Cookie клиента = name=value (клиент ведёт одну сессию кейса).</summary>
    public static void SetRequestCookie(HttpClient client, string name, string value)
    {
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"{name}={value}");
    }

    /// <summary>POST /auth/refresh с единственным cookie refresh_token=value (IF-007).</summary>
    public static Task<HttpResponseMessage> RefreshWithCookieAsync(HttpClient client, string refreshValue)
    {
        SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshValue);
        return client.PostAsync(B12AuthEndpoints.Refresh, content: null);
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
