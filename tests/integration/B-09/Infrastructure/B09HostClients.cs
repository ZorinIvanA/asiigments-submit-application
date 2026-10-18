using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Infrastructure;

/// <summary>
/// Клиенты и предусловия тестового хоста батча B-09 (кейсы TS-018..TS-025): без
/// авто-редиректов (точные статусы), с cookie-контейнером (cookie access_token/
/// refresh_token — носители сессии). Каждый клиент — собственный CookieContainer:
/// сессия одного клиента не задаёт cookie другому.
///
/// given «пользователь существует» (TS-020/TS-023/TS-024) строится публичным API:
/// POST /auth/register с тестовой подменой RemoteIpAddress (регистровый лимит —
/// ключ 'IP', кейсы не должны исчерпывать лимиты друг друга). Информация о
/// пользователе для проверок given/then — IUserRepository из factory.Services
/// (чтение возвращает копии-снимки; контракт IF-015).
/// </summary>
public static class B09HostClients
{
    public const string RegisterEndpoint = "/api/v1/auth/register";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RecoveryRequestEndpoint = "/api/v1/auth/recovery/request";

    /// <summary>Пароль всех тестовых пользователей (правила FR-006 соблюдены).</summary>
    public const string TestUserPassword = "Passw0rd!";

    public static HttpClient Create(B09WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    /// <summary>
    /// given «пользователь с данными (fullName, login, email)»: регистрация студента
    /// (201). ip != null — запрос с тестовой подменой RemoteIpAddress (кейсы
    /// TS-020/TS-023/TS-024: у каждого кейса свой IP — собственный ключ 'IP' лимитера).
    /// Возвращает клиент (cookie автоматического входа в контейнере).
    /// </summary>
    public static async Task<HttpClient> RegisterStudentAsync(
        B09WebAppFactory factory,
        string fullName,
        string login,
        string email,
        string password = TestUserPassword,
        string? ip = null)
    {
        var client = Create(factory);
        using var register = await PostJsonAsync(client, RegisterEndpoint, new
        {
            fullName,
            login,
            email,
            password,
            repeatPassword = password,
        }, ip);
        Assert.True(
            register.StatusCode == HttpStatusCode.Created,
            $"Предусловие кейса: регистрация {login} должна вернуть 201, фактически {(int)register.StatusCode}: {await register.Content.ReadAsStringAsync()}");
        return client;
    }

    /// <summary>Шаг «вход с паролем»: POST /auth/login; статус возвращает сценарий на проверку.</summary>
    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginEndpoint, new { login, password });

    /// <summary>
    /// Шаг «запрос кода восстановления»: POST /auth/recovery/request (200 — ВСЕГДА,
    /// FR-012: оракула существования email нет). Предусловие заполнения квоты ключа
    /// recovery_request кейсом TS-020.
    /// </summary>
    public static async Task RequestRecoveryCodeAsync(B09WebAppFactory factory, string email, HttpClient? client = null)
    {
        var owner = client ?? Create(factory);
        using var request = await owner.PostAsJsonAsync(RecoveryRequestEndpoint, new { email });
        Assert.True(
            request.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: recovery/request для {email} должен вернуть 200, фактически {(int)request.StatusCode}: {await request.Content.ReadAsStringAsync()}");
    }

    /// <summary>Пользователь по email (ci) из хранилища тестового хоста — для проверок given/then.</summary>
    public static User ResolveUserByEmail(B09WebAppFactory factory, string email)
    {
        var user = factory.Services.GetRequiredService<IUserRepository>().GetByEmail(email);
        Assert.NotNull(user);
        return user!;
    }

    /// <summary>POST c JSON-телом и опциональной подменой RemoteIpAddress (заголовок фабрики).</summary>
    public static Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client,
        string uri,
        object body,
        string? ip = null)
    {
        if (ip is null)
        {
            return client.PostAsJsonAsync(uri, body);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        request.Headers.Add(B09WebAppFactory.RemoteIpHeader, ip);
        return client.SendAsync(request);
    }
}
