using System.Text;
using LabsApp.Auth;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>
/// HTTP-помощники login/refresh/logout-плитки батча B-10 (TS-039..TS-047,
/// TS-053..TS-058, IF-007): клиент без авто-редиректов (точные статусы) и без
/// cookie-контейнера — cookie читаются из Set-Cookie ответов (B10SetCookieReader)
/// и передаются заголовком Cookie явно. Так «установлены оба cookie» (login),
/// «без Set-Cookie» (401 refresh) и «refresh_token не переустанавливается»
/// (refresh/logout) проверяются дословно по заголовкам ответа. Клиенту кейса
/// назначается фиксированный RemoteIpAddress (заголовок подмены
/// B10TimedWebAppFactory — ключ лимитера входа 'lower(trim(login))|IP', FR-004).
/// Механика — собственная копия зоны B-09 (B09AuthHttp): помощники чужих зон
/// не переиспользуются (BL-001 BUG-001). Пути эндпойнтов — константы зоны
/// B10CookieFlow (единый источник зоны).
/// </summary>
public static class B10AuthRequests
{
    /// <summary>Эндпойнт проверки живости хоста (кейс TS-047).</summary>
    public const string HealthPath = "/health";

    /// <summary>Пароль сид-преподавателя кейсов (дословно по кейсам TS-039..TS-045; SeedOptions.DefaultTeacherPassword).</summary>
    public const string TeacherPassword = "teacher123!";

    /// <summary>Клиент без cookie-контейнера; ip != null — фиксированный RemoteIpAddress всех запросов.</summary>
    public static HttpClient Create(B10TimedWebAppFactory factory, string? ip = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        if (ip is not null)
        {
            client.DefaultRequestHeaders.Add(B10TimedWebAppFactory.RemoteIpHeader, ip);
        }

        return client;
    }

    /// <summary>POST /auth/login с JSON-телом {login, password} (значения кейсов экранирования не требуют).</summary>
    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(B10CookieFlow.LoginPath, new { login, password });

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

    /// <summary>Все разобранные заголовки Set-Cookie ответа (пустой список, если заголовков нет).</summary>
    public static IReadOnlyList<B10SetCookie> SetCookies(HttpResponseMessage response) =>
        B10SetCookieReader.Read(response);

    /// <summary>Установлен ли cookie с именем name (ответом Set-Cookie).</summary>
    public static bool HasSetCookie(HttpResponseMessage response, string name) =>
        SetCookies(response).Any(cookie => cookie.Name == name);

    /// <summary>Значение cookie name из Set-Cookie ответа либо null, если cookie не установлен.</summary>
    public static string? SetCookieValue(HttpResponseMessage response, string name) =>
        SetCookies(response).FirstOrDefault(cookie => cookie.Name == name)?.Value;
}
