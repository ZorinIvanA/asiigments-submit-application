using LabsApp;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B15.Groups.Infrastructure;

/// <summary>
/// Клиент доменных кейсов групп батча B-15: аутентификации в приложении нет —
/// все эндпоинты публичны, «текущий пользователь» — засеянный преподаватель
/// (Seed__TeacherLogin). Сценарии ходят обычным анонимным клиентом фабрики
/// (cookie/JWT-сессии не существуют; POST /api/v1/auth/* удалён).
/// </summary>
public static class B15GroupsSession
{
    /// <summary>
    /// Анонимный клиент фабрики (заголовков авторизации нет — приложение
    /// работает без аутентификации).
    /// </summary>
    public static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
}
