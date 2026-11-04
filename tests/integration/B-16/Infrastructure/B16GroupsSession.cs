using LabsApp;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B16.Infrastructure;

/// <summary>
/// Клиент доменных кейсов групп батча B-16 (TS-102..TS-106, TS-130..TS-138,
/// TS-160, TS-169, TS-199, TS-201): аутентификации в приложении нет — все
/// эндпоинты публичны, «текущий пользователь» — засеянный преподаватель
/// (Seed__TeacherLogin). Сценарии ходят обычным анонимным клиентом фабрики
/// (cookie/JWT-сессии не существуют; POST /api/v1/auth/* удалён).
/// </summary>
public static class B16GroupsSession
{
    /// <summary>
    /// Анонимный клиент фабрики (без авто-редиректов и cookie-контейнера —
    /// точные статусы; заголовков авторизации нет).
    /// </summary>
    public static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
}
