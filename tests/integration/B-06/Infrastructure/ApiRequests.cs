namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Построители запросов батча B-06 (register/login/me — FR-012/FR-011; labs — FR-011).
/// </summary>
public static class ApiRequests
{
    public const string RegisterEndpoint = "/api/v1/auth/register";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string MeEndpoint = "/api/v1/auth/me";
    public const string LabsEndpoint = "/api/v1/labs";

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginEndpoint, new { login, password });

    /// <summary>POST /api/v1/auth/register с соединением по умолчанию (единый IP TestServer).</summary>
    public static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string fullName,
        string login,
        string email,
        string password,
        string repeatPassword) =>
        RegisterFromIpAsync(client, remoteIp: null, fullName, login, email, password, repeatPassword);

    /// <summary>
    /// POST /api/v1/auth/register с эмуляцией RemoteIpAddress (кейсы FR-012 задают
    /// «RemoteIpAddress=X»: лимит регистраций 5/час на IP — см. B06WebAppFactory).
    /// </summary>
    public static Task<HttpResponseMessage> RegisterFromIpAsync(
        HttpClient client,
        string? remoteIp,
        string fullName,
        string login,
        string email,
        string password,
        string repeatPassword)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RegisterEndpoint)
        {
            Content = JsonContent.Create(new
            {
                fullName,
                login,
                email,
                password,
                repeatPassword,
            }),
        };
        if (remoteIp is not null)
        {
            request.Headers.Add(B06WebAppFactory.RemoteIpHeader, remoteIp);
        }

        return client.SendAsync(request);
    }

    /// <summary>POST /api/v1/labs с телом кейса TS-028: {number:0, semester:1, content:'x', defenseRequired:false}.</summary>
    public static Task<HttpResponseMessage> CreateLabWithInvalidNumberAsync(HttpClient client) =>
        client.PostAsJsonAsync(LabsEndpoint, new
        {
            number = 0,
            semester = 1,
            content = "x",
            defenseRequired = false,
        });
}
