using LabsApp;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B17.Groups.Infrastructure;

/// <summary>
/// Клиенты доменных кейсов групп батча B-17 (TS-122..TS-128, TS-202, TS-204):
/// аутентификации в приложении нет — все эндпоинты публичны, «текущий
/// пользователь» — засеянный преподаватель (Seed__TeacherLogin). Сценарии ходят
/// обычным анонимным клиентом фабрики (cookie/JWT-сессии не существуют;
/// POST /api/v1/auth/* удалён).
/// </summary>
public static class B17GroupsSession
{
    /// <summary>
    /// Анонимный клиент фабрики (заголовков авторизации нет — приложение
    /// работает без аутентификации; бывший given «сессия teacher»).
    /// </summary>
    public static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// Анонимный клиент фабрики для «второго» клиента сценария (ex-«сессия
    /// студента»): аутентификации нет, клиент не отличается от CreateClient —
    /// отдельное имя сохранено для читаемости сценариев.
    /// </summary>
    public static HttpClient CreateStudentClient(WebApplicationFactory<Program> factory) =>
        CreateClient(factory);
}
