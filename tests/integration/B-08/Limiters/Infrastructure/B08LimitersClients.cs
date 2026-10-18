using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Limiters.Infrastructure;

/// <summary>
/// Клиенты и предусловия auth-сценариев волны B-08 «лимитер/матрица» (кейсы
/// TS-019..TS-022, TS-024): эндпоинты /api/v1/auth (register — FR-006, login —
/// FR-007, recovery/request — FR-012), клиенты с фиксированным транспортным IP
/// (given кейсов, см. <see cref="B08LimitersTestClientIpResolver"/>). Без
/// авто-редиректов (точные статусы); каждый клиент — собственный
/// CookieContainer (вход в одном клиенте не задаёт cookie другому).
/// </summary>
public static class B08LimitersClients
{
    public const string RegisterEndpoint = "/api/v1/auth/register";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RecoveryRequestEndpoint = "/api/v1/auth/recovery/request";

    /// <summary>Пароль всех тестовых пользователей (правила §8 соблюдены).</summary>
    public const string TestUserPassword = "Passw0rd!";

    /// <summary>Клиент с cookie-контейнером.</summary>
    public static HttpClient CreateClient(B08LimitersWebAppFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
    }

    /// <summary>Клиент БЕЗ cookie-контейнера (кейс сам управляет Cookie-заголовком).</summary>
    public static HttpClient CreateClientWithoutCookies(B08LimitersWebAppFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }

    /// <summary>Клиент с фиксированным транспортным IP (given «RemoteIpAddress=…»).</summary>
    public static HttpClient CreateClientWithIp(B08LimitersWebAppFactory factory, string ip)
    {
        var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add(B08LimitersTestClientIpResolver.TestIpHeader, ip);
        return client;
    }

    /// <summary>POST /auth/login {login, password} (сырой ответ — статус проверяет кейс).</summary>
    public static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginEndpoint, new { login, password });

    /// <summary>
    /// Шаг given «пользователь с email … существует»: регистрация студента через
    /// публичный API (201), без входа (кейсу лимитеров сессия не нужна).
    /// </summary>
    public static async Task<HttpClient> RegisterStudentAsync(
        B08LimitersWebAppFactory factory,
        string fullName,
        string login,
        string email)
    {
        var client = CreateClient(factory);
        using var register = await client.PostAsJsonAsync(RegisterEndpoint, new
        {
            fullName,
            login,
            email,
            password = TestUserPassword,
            repeatPassword = TestUserPassword,
        });
        Assert.True(
            register.StatusCode == HttpStatusCode.Created,
            $"Предусловие кейса: регистрация {login} должна вернуть 201, фактически {(int)register.StatusCode}: {await register.Content.ReadAsStringAsync()}");
        return client;
    }

    /// <summary>Пользователь по логину (ci) из хранилища тестового хоста (IF-015).</summary>
    public static User? FindUserByLogin(B08LimitersWebAppFactory factory, string login) =>
        factory.Services.GetRequiredService<IUserRepository>().GetByLogin(login);
}
