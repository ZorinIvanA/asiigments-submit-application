using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B01.Infrastructure;

/// <summary>
/// Клиенты тестового хоста: без авто-редиректов (точные статусы), с cookie-контейнером
/// (access-cookie httpOnly — единственный носитель сессии, FR-008).
/// </summary>
public static class HostClients
{
    public static HttpClient Create(B01WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
}
