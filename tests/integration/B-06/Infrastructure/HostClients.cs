using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Клиенты тестового хоста батча B-06: без авто-редиректов (точные статусы).
/// Каждый клиент — собственный цепочка обработчиков TestServer: вход в одном
/// клиенте не задаёт cookie другому (нужен для кейсов «cookie отсутствуют»).
/// </summary>
public static class HostClients
{
    public static HttpClient Create(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    /// <summary>
    /// Клиент TestServer с заранее заданной cookie (минт сессии ADR-022; харнесный
    /// JWT кейса TS-032): cookie передаётся заголовком Cookie каждого запроса
    /// (HandleCookies=false — собственный контейнер не мешает). Значения access-JWT
    /// (base64url + точки) — допустимое содержимое заголовка.
    /// </summary>
    public static HttpClient CreateWithCookie(WebApplicationFactory<Program> factory, string name, string value)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", $"{name}={value}");
        return client;
    }
}
