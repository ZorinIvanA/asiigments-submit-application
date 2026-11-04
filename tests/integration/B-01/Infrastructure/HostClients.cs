using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B01.Infrastructure;

/// <summary>
/// Клиенты тестового хоста: без авто-редиректов (точные статусы цепочек 3xx).
/// Аутентификации в приложении нет — все запросы уходят анонимно,
/// cookie-контейнер сессии не нужен.
/// </summary>
public static class HostClients
{
    public static HttpClient Create(B01WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
}
