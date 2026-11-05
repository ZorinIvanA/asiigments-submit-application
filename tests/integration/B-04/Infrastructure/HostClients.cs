using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B04.Infrastructure;

/// <summary>
/// Клиенты тестового хоста кейсов B-04 (FR-017/FR-020): без авто-редиректов
/// (точные статусы). Приложение работает без аутентификации — запросы анонимны,
/// «текущий пользователь» доменных операций (updatedBy сдач, /me/**) —
/// сид-преподаватель (Seed__TeacherLogin).
/// </summary>
public static class HostClients
{
    public static HttpClient Create(B04HostFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
}
