using System.Text;
using LabsApp.Auth;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B11.Infrastructure;

/// <summary>
/// HTTP-помощники батча B-11 (auth, IF-007): клиент без авто-редиректов (точные
/// статусы) и без cookie-контейнера — куки читаются из Set-Cookie ответов и
/// передаются заголовком Cookie явно. Так матрица Set-Cookie (IF-004/NFR-007:
/// login 2, refresh ТОЛЬКО access, logout 2 сброса) и «без Set-Cookie» на 401
/// проверяются дословно, а not-переустановка refresh_token (ASM-003) видна
/// по заголовкам ответа.
/// </summary>
public static class HostClients
{
    public const string LoginPath = "/api/v1/auth/login";
    public const string RefreshPath = "/api/v1/auth/refresh";
    public const string LogoutPath = "/api/v1/auth/logout";

    public static HttpClient Create(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>POST /auth/login с JSON-телом {login, password} (значения без экранирования не требуются).</summary>
    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        PostJsonAsync(client, LoginPath, JsonSerializer.Serialize(new { login, password }));

    /// <summary>POST с произвольным JSON-телом (битые/неполные тела кейсов TS-044/TS-045).</summary>
    public static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, string json) =>
        client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>POST без тела — refresh/logout идут только по cookie (IF-007).</summary>
    public static Task<HttpResponseMessage> PostWithoutBodyAsync(HttpClient client, string path) =>
        client.PostAsync(path, content: null);

    /// <summary>Единственный заголовок Cookie клиента = name=value (клиент ведёт одну сессию кейса).</summary>
    public static void SetRequestCookie(HttpClient client, string name, string value)
    {
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"{name}={value}");
    }

    /// <summary>Все заголовки Set-Cookie ответа (пустой список, если заголовков нет).</summary>
    public static IReadOnlyList<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToArray()
            : Array.Empty<string>();

    /// <summary>Установлен ли cookie с именем name (ответом Set-Cookie).</summary>
    public static bool HasSetCookie(HttpResponseMessage response, string name) =>
        SetCookieHeader(response, name) is not null;

    /// <summary>
    /// Полная строка Set-Cookie для cookie name (первый совпавший заголовок) либо
    /// null — единственный поиск по имени в зоне (CR-002): сравнение ordinal
    /// (имена cookie регистрозависимы, RFC 6265); атрибуты строки проверяются
    /// HasSetCookieAttribute.
    /// </summary>
    public static string? SetCookieHeader(HttpResponseMessage response, string name)
    {
        foreach (var cookie in SetCookies(response))
        {
            if (CookieNameOf(cookie) == name)
            {
                return cookie;
            }
        }

        return null;
    }

    /// <summary>
    /// Значение cookie name из Set-Cookie ответа (первый атрибут name=value до ';')
    /// либо null, если cookie не установлен.
    /// </summary>
    public static string? SetCookieValue(HttpResponseMessage response, string name)
    {
        var header = SetCookieHeader(response, name);
        if (header is null)
        {
            return null;
        }

        var pair = header.Split(';', 2)[0];
        var separator = pair.IndexOf('=');
        return separator < 0 ? string.Empty : pair[(separator + 1)..];
    }

    /// <summary>
    /// Есть ли у строки Set-Cookie атрибут attribute — сверка частей после ';'
    /// БЕЗ учёта регистра (HttpOnly, SameSite=Strict, Path=/, Max-Age=900/0…).
    /// Общий хелпер зоны (CR-002): хост сериализует атрибуты в нижнем регистре,
    /// но проверка не должна зависеть от его фактической сериализации.
    /// </summary>
    public static bool HasSetCookieAttribute(string setCookie, string attribute)
    {
        foreach (var part in setCookie.Split(';'))
        {
            if (string.Equals(part.Trim(), attribute, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string CookieNameOf(string setCookie) =>
        setCookie.Split(';', 2)[0].Split('=', 2)[0].Trim();
}
