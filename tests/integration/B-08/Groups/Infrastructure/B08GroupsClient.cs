using System.Text;
using System.Text.Json;
using LabsApp;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B08.Groups.Infrastructure;

/// <summary>
/// HTTP-клиент сценариев групп/репозиториев батча B-08 над фикстурой хоста.
/// Cookie-контейнер включён (сессии создаются POST /api/v1/auth/login и
/// переносятся автоматически, NFR-007 Path=/); авто-редиректы выключены —
/// точные статусы контракта. Тела сценариев передаются сырыми JSON-строками
/// (точные составы полей кейсов, включая обрамляющие пробелы имени группы).
/// </summary>
public sealed class B08GroupsClient : IDisposable
{
    /// <summary>Эндпоинты контракта /api/v1, используемые кейсами батча.</summary>
    public const string GroupsEndpoint = "/api/v1/groups";
    public const string StudentsEndpoint = "/api/v1/students";
    public const string LabsEndpoint = "/api/v1/labs";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RegisterEndpoint = "/api/v1/auth/register";
    public const string MeEndpoint = "/api/v1/auth/me";

    /// <summary>Тексты ошибок контракта (дословно FR-006/FR-017/FR-019).</summary>
    public const string GroupNotFoundMessage = "Группа не найдена";
    public const string GroupNameDuplicateMessage = "Группа с таким названием уже существует";
    public const string GroupNameLengthError = "Название группы — от 1 до 100 символов";
    public const string LabDuplicatePairMessage = "Лабораторная с таким номером уже есть в семестре";
    public const string LoginDuplicateMessage = "Пользователь с таким логином уже существует";

    private readonly HttpClient _http;

    public B08GroupsClient(WebApplicationFactory<Program> factory)
    {
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
    }

    /// <summary>Клиент с сессией сид-преподавателя (given «сессия teacher»).</summary>
    public static async Task<B08GroupsClient> LoginAsTeacherAsync(WebApplicationFactory<Program> factory)
    {
        var client = new B08GroupsClient(factory);
        await client.LoginAsync(B08GroupsWebAppFactory.TeacherLogin, B08GroupsWebAppFactory.TestTeacherPassword);
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

    public Task<HttpResponseMessage> DeleteAsync(string url) => _http.DeleteAsync(url);

    public async Task<HttpResponseMessage> PostJsonAsync(string url, string json) =>
        await SendJsonAsync(HttpMethod.Post, url, json);

    public async Task<HttpResponseMessage> PutJsonAsync(string url, string json) =>
        await SendJsonAsync(HttpMethod.Put, url, json);

    /// <summary>Экранированная JSON-строка (точные тела кейсов без ручного квотирования).</summary>
    public static string JsonString(string value) => JsonSerializer.Serialize(value);

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
    public static async Task<JsonDocument> ReadJsonObjectAsync(HttpResponseMessage response, HttpStatusCode expected, string because)
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

    /// <summary>
    /// Читает ответ со строгим статусом; возвращает разобранный JSON-массив
    /// (GET /api/v1/groups отдаёт GroupDto[] — FR-019, список не пагинируется).
    /// </summary>
    public static async Task<JsonDocument> ReadJsonArrayAsync(HttpResponseMessage response, HttpStatusCode expected, string because)
    {
        Assert.True(
            response.StatusCode == expected,
            $"{because} → ожидался {expected}, фактически {response.StatusCode}: {await ReadBodyAsync(response)}");
        var document = await ReadJsonAsync(response);
        Assert.True(
            document.RootElement.ValueKind == JsonValueKind.Array,
            $"{because}: корень тела обязан быть JSON-массивом.");
        return document;
    }

    /// <summary>Тело ответа строкой (для сообщений диагностики — строка буферизуется).</summary>
    public static async Task<string> ReadBodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    /// <summary>Свойство-строка JSON-объекта (контракт поле обязано присутствовать).</summary>
    public static string StringProperty(JsonElement element, string propertyName)
    {
        Assert.True(
            element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String,
            $"Ожидалось строковое свойство «{propertyName}» в ответе.");
        return value.GetString()!;
    }

    /// <summary>Свойство JSON-объекта обязано отсутствовать ИЛИ быть null.</summary>
    public static void AssertPropertyIsNull(JsonElement element, string propertyName)
    {
        Assert.True(element.TryGetProperty(propertyName, out var value), $"Свойство «{propertyName}» отсутствует в ответе.");
        Assert.True(
            value.ValueKind == JsonValueKind.Null,
            $"Свойство «{propertyName}» обязано быть null, фактически {value.ValueKind}.");
    }

    /// <summary>Логины элементов выдачи (items PagedResult<StudentDto>).</summary>
    public static string[] ReadLogins(JsonElement pagedResult) =>
        pagedResult.GetProperty("items")
            .EnumerateArray()
            .Select(item => StringProperty(item, "login"))
            .ToArray();

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
