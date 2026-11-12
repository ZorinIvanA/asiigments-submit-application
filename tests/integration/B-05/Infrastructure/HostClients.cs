using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B05.Infrastructure;

/// <summary>
/// Клиенты тестового хоста батча B-05: без авто-редиректов (точные статусы),
/// с cookie-контейнером (access-cookie httpOnly — единственный носитель сессии,
/// FR-009/FR-025). Каждый клиент — собственный CookieContainer: вход в одном
/// клиенте не задаёт cookie другому (нужен для кейсов «без cookie» и для
/// раздельных сессий преподавателя и студента).
/// </summary>
public static class HostClients
{
    /// <summary>Логин сидового преподавателя (Seed__TeacherLogin по умолчанию, FR-004).</summary>
    public const string TeacherLogin = SeedOptions.DefaultTeacherLogin;

    /// <summary>Логин/пароль сидового студента student01 (демо-набор FR-004).</summary>
    public const string SeededStudentLogin = "student01";
    public const string SeededStudentPassword = "student123!";

    public static HttpClient Create(B05WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    /// <summary>
    /// given «teacher авторизован»: вход сидовым преподавателем с паролем фикстуры
    /// (Seed__TeacherPassword задан фабрикой явно). Возвращает клиента с access-cookie.
    /// </summary>
    public static async Task<HttpClient> CreateTeacherClientAsync(B05WebAppFactory factory)
    {
        var client = Create(factory);
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            login = TeacherLogin,
            password = B05WebAppFactory.TestTeacherPassword,
        });
        Assert.True(
            login.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: вход преподавателя должен вернуть 200, фактически {login.StatusCode}: {await login.Content.ReadAsStringAsync()}");
        return client;
    }

    /// <summary>
    /// given «student авторизован»: вход сидовым студентом (демо-набор FR-004).
    /// </summary>
    public static async Task<HttpClient> CreateSeededStudentClientAsync(B05WebAppFactory factory)
    {
        var client = Create(factory);
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            login = SeededStudentLogin,
            password = SeededStudentPassword,
        });
        Assert.True(
            login.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: вход студента должен вернуть 200, фактически {login.StatusCode}: {await login.Content.ReadAsStringAsync()}");
        return client;
    }
}
