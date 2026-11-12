using System.Text;
using System.Text.Json;
using LabsApp;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B08.Repositories.Infrastructure;

/// <summary>
/// HTTP-клиент сценариев «репозитории» батча B-08 над фикстурой хоста.
/// Cookie-контейнер включён (сессии создаются POST /api/v1/auth/login и
/// переносятся автоматически, NFR-007 Path=/); авто-редиректы выключены —
/// точные статусы контракта. Тела сценариев передаются сырыми JSON-строками
/// (точные составы полей кейсов). Каждый сценарий потока создаёт СВОЙ клиент —
/// независимые cookie-контейнеры параллельных сессий (TS-176/TS-178).
/// </summary>
public sealed class B08RepositoriesClient : IDisposable
{
    /// <summary>Эндпоинты контракта /api/v1, используемые кейсами TS-175..TS-178.</summary>
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RegisterEndpoint = "/api/v1/auth/register";
    public const string GroupsEndpoint = "/api/v1/groups";
    public const string LabsEndpoint = "/api/v1/labs";
    public const string StudentsEndpoint = "/api/v1/students";
    public const string SubmissionsEndpoint = "/api/v1/submissions";

    /// <summary>Тексты ошибок контракта (дословно FR-006/FR-017).</summary>
    public const string LoginDuplicateMessage = "Пользователь с таким логином уже существует";
    public const string LabDuplicatePairMessage = "Лабораторная с таким номером уже есть в семестре";

    private readonly HttpClient _http;

    public B08RepositoriesClient(WebApplicationFactory<Program> factory)
    {
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
    }

    /// <summary>Клиент с сессией сид-преподавателя (given «сессия teacher»).</summary>
    public static async Task<B08RepositoriesClient> LoginAsTeacherAsync(B08RepositoriesWebAppFactory factory)
    {
        var client = new B08RepositoriesClient(factory);
        await client.LoginAsync(
            B08RepositoriesWebAppFactory.TeacherLogin,
            B08RepositoriesWebAppFactory.TestTeacherPassword);
        return client;
    }

    /// <summary>Создаёт сессию логином/паролем (POST /api/v1/auth/login); отказ — падение given.</summary>
    public async Task LoginAsync(string login, string password)
    {
        using var response = await PostJsonAsync(
            LoginEndpoint,
            $"{{\"login\":{JsonString(login)},\"password\":{JsonString(password)}}}");
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"POST /api/v1/auth/login (логин «{login}») → {response.StatusCode}: {await ReadBodyAsync(response)}");
    }

    public Task<HttpResponseMessage> GetAsync(string url) => _http.GetAsync(url);

    public async Task<HttpResponseMessage> PostJsonAsync(string url, string json) =>
        await SendJsonAsync(HttpMethod.Post, url, json);

    public async Task<HttpResponseMessage> PutJsonAsync(string url, string json) =>
        await SendJsonAsync(HttpMethod.Put, url, json);

    /// <summary>Экранированная JSON-строка (точные тела кейсов без ручного квотирования).</summary>
    public static string JsonString(string value) => JsonSerializer.Serialize(value);

    /// <summary>Тело ответа строкой (для сообщений диагностики — строка буферизуется).</summary>
    public static async Task<string> ReadBodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    /// <summary>Разбирает JSON-тело ответа; корень обязан быть объектом или массивом.</summary>
    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await ReadBodyAsync(response);
        try
        {
            var document = JsonDocument.Parse(content);
            Assert.Contains(document.RootElement.ValueKind, new[] { JsonValueKind.Object, JsonValueKind.Array });
            return document;
        }
        catch (JsonException exception)
        {
            throw new Xunit.Sdk.XunitException(
                $"Тело ответа не является JSON: «{Truncate(content)}» ({exception.Message}).");
        }
    }

    /// <summary>Читает ответ со строгим статусом; возвращает разобранный JSON-объект.</summary>
    public static async Task<JsonDocument> ReadJsonObjectAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string because)
    {
        Assert.True(
            response.StatusCode == expected,
            $"{because} → ожидался {expected}, фактически {response.StatusCode}: {await ReadBodyAsync(response)}");
        var document = await ReadJsonAsync(response);
        Assert.True(
            document.RootElement.ValueKind == JsonValueKind.Object,
            $"{because}: корень тела обязан быть JSON-объектом.");
        return document;
    }

    /// <summary>Свойство-строка JSON-объекта (контракт: поле обязано присутствовать строкой).</summary>
    public static string StringProperty(JsonElement element, string propertyName)
    {
        Assert.True(
            element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String,
            $"Ожидалось строковое свойство «{propertyName}» в ответе.");
        return value.GetString()!;
    }

    /// <summary>Строка-поле тела регистрации (точные составы полей кейсов).</summary>
    public static string RegisterBody(string fullName, string login, string email) =>
        "{\"fullName\":" + JsonString(fullName) +
        ",\"login\":" + JsonString(login) +
        ",\"email\":" + JsonString(email) +
        ",\"password\":\"Passw0rd!\",\"repeatPassword\":\"Passw0rd!\"}";

    private async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string url, string json)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return await _http.SendAsync(request);
    }

    private static string Truncate(string value) =>
        value.Length <= 400 ? value : value[..400] + "…";

    public void Dispose() => _http.Dispose();
}
