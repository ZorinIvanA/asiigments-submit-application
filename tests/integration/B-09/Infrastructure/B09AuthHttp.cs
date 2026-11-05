using LabsApp.Auth;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B09.Infrastructure;

/// <summary>
/// HTTP-помощники кейсов батча TS-039..TS-047/TS-053..TS-058 (вход/refresh/logout,
/// IF-007): клиент без авто-редиректов (точные статусы) и без cookie-контейнера —
/// куки читаются из Set-Cookie ответов и передаются заголовком Cookie явно. Так
/// «установлены оба cookie» (login), «без Set-Cookie» (401 refresh) и
/// «refresh_token не переустанавливается» (refresh/logout) проверяются дословно
/// по заголовкам ответа. Клиенту кейса назначается фиксированный RemoteIpAddress
/// (тестовая подмена базовой фабрики, ключ лимитера входа 'login|IP').
/// </summary>
public static class B09AuthHttp
{
    public const string LoginPath = "/api/v1/auth/login";
    public const string RefreshPath = "/api/v1/auth/refresh";
    public const string LogoutPath = "/api/v1/auth/logout";
    public const string HealthPath = "/health";

    /// <summary>Пароль сид-преподавателя кейсов (дословно по кейсам TS-039..TS-045).</summary>
    public const string TeacherPassword = "teacher123!";

    /// <summary>Клиент без cookie-контейнера; ip != null — фиксированный RemoteIpAddress всех запросов.</summary>
    public static HttpClient Create(B09WebAppFactory factory, string? ip = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        if (ip is not null)
        {
            client.DefaultRequestHeaders.Add(B09WebAppFactory.RemoteIpHeader, ip);
        }

        return client;
    }

    /// <summary>POST /auth/login с JSON-телом {login, password} (значения кейсов экранирования не требуют).</summary>
    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginPath, new { login, password });

    /// <summary>POST с произвольным телом (битые/неполные тела кейсов TS-045/TS-046).</summary>
    public static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, string json) =>
        client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>POST без тела — refresh/logout идут только по cookie (IF-007).</summary>
    public static Task<HttpResponseMessage> PostNoBodyAsync(HttpClient client, string path) =>
        client.PostAsync(path, content: null);

    /// <summary>Единственный заголовок Cookie клиента = name=value (клиент ведёт одну сессию кейса).</summary>
    public static void SetRequestCookie(HttpClient client, string name, string value)
    {
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{name}={value}");
    }

    /// <summary>Заголовок Cookie клиента из нескольких пар (logout посылает обе cookie сессии).</summary>
    public static void SetRequestCookies(HttpClient client, params (string Name, string Value)[] cookies)
    {
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}")));
    }

    /// <summary>Заголовок Cookie ОДНОГО запроса (параллельные refresh кейса TS-057).</summary>
    public static void SetRequestCookie(HttpRequestMessage request, string name, string value) =>
        request.Headers.TryAddWithoutValidation("Cookie", $"{name}={value}");

    /// <summary>Все заголовки Set-Cookie ответа (пустой список, если заголовков нет).</summary>
    public static IReadOnlyList<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToArray()
            : Array.Empty<string>();

    /// <summary>Установлен ли cookie с именем name (ответом Set-Cookie).</summary>
    public static bool HasSetCookie(HttpResponseMessage response, string name)
    {
        foreach (var cookie in SetCookies(response))
        {
            if (CookieNameOf(cookie) == name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Значение cookie name из Set-Cookie ответа (первый атрибут name=value до ';')
    /// либо null, если cookie не установлен.
    /// </summary>
    public static string? SetCookieValue(HttpResponseMessage response, string name)
    {
        foreach (var cookie in SetCookies(response))
        {
            if (CookieNameOf(cookie) != name)
            {
                continue;
            }

            var pair = cookie.Split(';', 2)[0];
            var separator = pair.IndexOf('=');
            return separator < 0 ? string.Empty : pair[(separator + 1)..];
        }

        return null;
    }

    /// <summary>Полная строка Set-Cookie cookie name (для проверки Max-Age=0 сбросов logout).</summary>
    public static bool TryGetSetCookieLine(HttpResponseMessage response, string name, out string? setCookie)
    {
        foreach (var cookie in SetCookies(response))
        {
            if (CookieNameOf(cookie) == name)
            {
                setCookie = cookie;
                return true;
            }
        }

        setCookie = null;
        return false;
    }

    private static string CookieNameOf(string setCookie) =>
        setCookie.Split(';', 2)[0].Split('=', 2)[0].Trim();
}
