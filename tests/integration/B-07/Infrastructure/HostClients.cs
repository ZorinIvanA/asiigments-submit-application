using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Infrastructure;

/// <summary>
/// Клиенты и предусловия тестового хоста батча B-07: без авто-редиректов (точные
/// статусы), с cookie-контейнером (access-cookie httpOnly — единственный носитель
/// сессии). Каждый клиент — собственный CookieContainer: вход в одном клиенте не
/// задаёт cookie другому.
///
/// given «сессия пользователя с email …» (кейсы TS-089..TS-096, TS-193, TS-194)
/// строится ТОЛЬКО публичным API: POST /api/v1/auth/register (учётка студента
/// с нужным email, пароль правил §8) → POST /api/v1/auth/login (access-cookie в
/// контейнере). Информация о пользователе для проверок хранилища — IUserRepository
/// из factory.Services (чтение возвращает копии-снимки; контракт IF-015).
/// </summary>
public static class HostClients
{
    public const string ProfileEndpoint = "/api/v1/me/profile";
    public const string RegisterEndpoint = "/api/v1/auth/register";
    public const string LoginEndpoint = "/api/v1/auth/login";

    /// <summary>Пароль всех тестовых пользователей (правила §8 соблюдены).</summary>
    public const string TestUserPassword = "Passw0rd!";

    public static HttpClient Create(B07WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    /// <summary>
    /// given «сессия студента с данными (fullName, email)»: регистрация студента
    /// (201) и вход (200) в одном клиенте. Возвращает (клиент с access-cookie,
    /// uuid пользователя).
    /// </summary>
    public static async Task<(HttpClient Client, Guid UserId)> RegisterAndLoginStudentAsync(
        B07WebAppFactory factory,
        string fullName,
        string login,
        string email)
    {
        var client = Create(factory);

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

        await LoginAsync(client, login);

        var userId = ResolveUserIdByEmail(factory, email);
        return (client, userId);
    }

    /// <summary>Шаг given «занятый другим пользователем email»: регистрация без входа (201).</summary>
    public static async Task<HttpClient> RegisterStudentAsync(
        B07WebAppFactory factory,
        string fullName,
        string login,
        string email)
    {
        var client = Create(factory);
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

    /// <summary>Шаг given «авторизован»: вход с access-cookie в контейнере клиента (200).</summary>
    public static async Task LoginAsync(HttpClient client, string login)
    {
        using var response = await client.PostAsJsonAsync(LoginEndpoint, new
        {
            login,
            password = TestUserPassword,
        });
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: вход {login} должен вернуть 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>Пользователь по email (ci) из хранилища тестового хоста — для проверок given/then.</summary>
    public static User ResolveUserByEmail(B07WebAppFactory factory, string email)
    {
        var user = factory.Services.GetRequiredService<IUserRepository>().GetByEmail(email);
        Assert.NotNull(user);
        return user;
    }

    /// <summary>uuid пользователя по email (ci) из хранилища тестового хоста.</summary>
    public static Guid ResolveUserIdByEmail(B07WebAppFactory factory, string email) =>
        ResolveUserByEmail(factory, email).Id;
}
