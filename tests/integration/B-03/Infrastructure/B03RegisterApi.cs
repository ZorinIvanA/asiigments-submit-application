using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B03.Infrastructure;

/// <summary>
/// Клиент и запросы регистрации кейсов B-03 (TS-031..TS-038, FR-006): POST
/// /api/v1/auth/register. Клиент БЕЗ cookie-контейнера: кейсы инспектируют сами
/// заголовки Set-Cookie (FR-008), см. <see cref="ManualCookies"/>; IP запроса
/// подменяется заголовком <see cref="B03HostFactory.RemoteIpHeader"/> (регистрационный
/// лимитер FR-004 ключуется по IP — у каждого кейса свой IP, см. B03HostFactory).
/// «Сырые» JSON-тела (PostRawAsync) нужны кейсам с нестандартной формой полей:
/// дополнительное поле role (TS-037).
/// </summary>
public static class B03RegisterApi
{
    /// <summary>Эндпойнт регистрации (FR-006).</summary>
    public const string Endpoint = "/api/v1/auth/register";

    /// <summary>Клиент без cookie-контейнера; все запросы идут с заданного RemoteIpAddress.</summary>
    public static HttpClient CreateClient(WebApplicationFactory<Program> factory, string remoteIp)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        Assert.True(
            client.DefaultRequestHeaders.TryAddWithoutValidation(B03HostFactory.RemoteIpHeader, remoteIp),
            $"Не удалось задать RemoteIpAddress '{remoteIp}' для тестового клиента регистрации.");
        return client;
    }

    /// <summary>POST /api/v1/auth/register со всеми полями-строками (каноническая форма FR-006).</summary>
    public static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string fullName,
        string login,
        string email,
        string password,
        string repeatPassword) =>
        client.PostAsJsonAsync(Endpoint, new
        {
            fullName,
            login,
            email,
            password,
            repeatPassword,
        });

    /// <summary>POST /api/v1/auth/register с заданным «сырым» JSON-телом (TS-037).</summary>
    public static Task<HttpResponseMessage> PostRawAsync(HttpClient client, string json) =>
        client.PostAsync(Endpoint, new StringContent(json, Encoding.UTF8, "application/json"));
}
