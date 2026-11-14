using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B23.Infrastructure;

/// <summary>
/// HTTP-помощники зоны B-23: клиент с cookie-контейнером (сессии в httpOnly-
/// cookie) и вход демо-учётками SQL-сида.
/// </summary>
public static class B23Http
{
    /// <summary>Клиент с cookie-контейнером — Set-Cookie доезжает между запросами.</summary>
    public static HttpClient CreateCookieClient(this B23WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("http://localhost"),
        });

    /// <summary>POST /auth/login; ответ — MeDto (login/fullName/role/groupName).</summary>
    public static async Task<JsonElement> LoginAsync(
        HttpClient client, string login, string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { login, password });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Клиент с cookie-контейнером и выполненным входом.</summary>
    public static async Task<HttpClient> LoginClientAsync(
        this B23WebAppFactory factory, string login, string password)
    {
        var client = factory.CreateCookieClient();
        await LoginAsync(client, login, password);
        return client;
    }
}
