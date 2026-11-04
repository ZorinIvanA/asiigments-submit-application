using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Клиент тестового хоста батча B-06: без авто-редиректов (точные статусы).
/// Аутентификации в приложении нет — клиент анонимный, cookie-механика не нужна.
/// </summary>
public static class HostClients
{
    public static HttpClient Create(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
}
