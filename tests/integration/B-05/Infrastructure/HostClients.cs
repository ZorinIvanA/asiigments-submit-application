using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B05.Infrastructure;

/// <summary>
/// Клиенты тестового хоста батча B-05: без авто-редиректов (точные статусы).
/// Аутентификации в приложении нет — запросы анонимны; «текущий пользователь»
/// доменных операций (/me/**, updatedBy сдач) — сид-преподаватель
/// (Seed__TeacherLogin).
/// </summary>
public static class HostClients
{
    public static HttpClient Create(B05WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
}
