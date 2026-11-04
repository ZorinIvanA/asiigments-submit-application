using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B23.Infrastructure;

/// <summary>
/// HTTP-помощники зоны B-23: анонимный клиент. Аутентификации в приложении
/// нет (POST /api/v1/auth/* удалён): все эндпоинты публичны, «текущий
/// пользователь» — засеянный преподаватель (Seed__TeacherLogin, SQL-сид
/// teacher).
/// </summary>
public static class B23Http
{
    /// <summary>Анонимный клиент фабрики (заголовков авторизации нет).</summary>
    public static HttpClient CreateClient(this B23WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            BaseAddress = new Uri("http://localhost"),
        });
}
