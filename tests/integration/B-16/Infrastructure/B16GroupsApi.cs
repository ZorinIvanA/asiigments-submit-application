using System.Text;
using System.Text.Json;

namespace LabsApp.IntegrationTests.B16.Infrastructure;

/// <summary>
/// Запросы кейсов групп батча B-16 (TS-102, TS-103, TS-104, TS-105, TS-106,
/// TS-160, TS-169; FR-019, FR-020, FR-006) и проверки HTTP-ответов: статус с
/// диагностикой тела, точные тексты message (словарь текстов ошибок FR-023) и
/// состав errors.&lt;поле&gt;. Копия механики зоны B-14/Groups (чужие зоны
/// недоступны для ссылок; изоляция зон — BL-001), плюс шаги FR-006/FR-020 кейса
/// TS-106 (POST /auth/register, PUT /students/{id}/group).
/// Тела запросов строятся сериализацией System.Text.Json: пробелы имени
/// («  ИК-224  », «   ») доходят до сервера дословно (given TS-103/TS-104/TS-169).
/// </summary>
public static class B16GroupsApi
{
    /// <summary>Базовый путь /groups (IF-010).</summary>
    public const string GroupsEndpoint = "/api/v1/groups";

    /// <summary>Базовый путь /students (IF-009/FR-020).</summary>
    public const string StudentsEndpoint = "/api/v1/students";

    /// <summary>POST /auth/register (IF-007/FR-006) — кейс TS-106.</summary>
    public const string RegisterEndpoint = "/api/v1/auth/register";

    /// <summary>GET /api/v1/groups (список, без пагинации).</summary>
    public static Task<HttpResponseMessage> GetGroupsAsync(HttpClient client) =>
        client.GetAsync(GroupsEndpoint);

    /// <summary>POST /api/v1/groups {name}.</summary>
    public static Task<HttpResponseMessage> PostGroupAsync(HttpClient client, string name) =>
        client.PostAsync(
            GroupsEndpoint,
            new StringContent(JsonSerializer.Serialize(new { name }), Encoding.UTF8, "application/json"));

    /// <summary>PUT /api/v1/groups/{id} {name}.</summary>
    public static Task<HttpResponseMessage> PutGroupAsync(HttpClient client, string id, string name) =>
        client.PutAsync(
            $"{GroupsEndpoint}/{id}",
            new StringContent(JsonSerializer.Serialize(new { name }), Encoding.UTF8, "application/json"));

    /// <summary>DELETE /api/v1/groups/{id}.</summary>
    public static Task<HttpResponseMessage> DeleteGroupAsync(HttpClient client, string id) =>
        client.DeleteAsync($"{GroupsEndpoint}/{id}");

    /// <summary>GET /api/v1/groups/{id}/students (query — «сырая» строка параметров либо null).</summary>
    public static Task<HttpResponseMessage> GetGroupStudentsAsync(HttpClient client, string id, string? query = null) =>
        client.GetAsync(string.IsNullOrEmpty(query)
            ? $"{GroupsEndpoint}/{id}/students"
            : $"{GroupsEndpoint}/{id}/students?{query}");

    /// <summary>PUT /api/v1/students/{id}/group {groupId} (кейс TS-106).</summary>
    public static Task<HttpResponseMessage> PutStudentGroupAsync(HttpClient client, string studentId, string groupId) =>
        client.PutAsync(
            $"{StudentsEndpoint}/{studentId}/group",
            new StringContent(JsonSerializer.Serialize(new { groupId }), Encoding.UTF8, "application/json"));

    /// <summary>GET /api/v1/students?{query} (кейс TS-105: фильтр groupId=none).</summary>
    public static Task<HttpResponseMessage> GetStudentsAsync(HttpClient client, string query) =>
        client.GetAsync($"{StudentsEndpoint}?{query}");

    /// <summary>
    /// POST /auth/register {fullName, login, email, password, repeatPassword}
    /// (кейс TS-106: студенты «Авроров Иван»/avrorov и «Ёлкин Пётр»/elkin созданы
    /// POST /auth/register с паролем «Passw0rd!»).
    /// </summary>
    public static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string fullName,
        string login,
        string email,
        string password) =>
        client.PostAsync(
            RegisterEndpoint,
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    fullName,
                    login,
                    email,
                    password,
                    repeatPassword = password,
                }),
                Encoding.UTF8,
                "application/json"));

    /// <summary>
    /// Проверяет статус и парсит JSON-тело. При несовпадении статуса сообщение
    /// включает фактическое тело (диагностика «expected/actual» для баг-репортов).
    /// </summary>
    public static async Task<JsonDocument> ParseWithStatusAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string step)
    {
        Assert.True(
            response.StatusCode == expected,
            $"Ожидался HTTP {(int)expected} на шаге «{step}», фактически {(int)response.StatusCode}: " +
            await response.Content.ReadAsStringAsync());
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    /// <summary>Проверяет точное равенство body.message ожидаемому тексту.</summary>
    public static void MessageIs(JsonElement body, string expected) =>
        Assert.True(
            body.TryGetProperty("message", out var message) && message.GetString() == expected,
            $"Ожидался message «{expected}», фактически: {body.GetRawText()}");

    /// <summary>
    /// errors.&lt;field&gt; — точное совпадение ПОЛНОГО перечня текстов в заданном
    /// порядке: порядок и сами тексты — дословно ожидаемые, посторонних нет
    /// (кейс TS-169: единственный текст ошибки имени группы).
    /// </summary>
    public static void FieldErrorsExactly(JsonElement body, string field, params string[] expectedTexts)
    {
        Assert.True(
            body.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object,
            $"Ожидался объект errors в теле ответа, фактически: {body.GetRawText()}");
        Assert.True(
            errors.TryGetProperty(field, out var fieldErrors) && fieldErrors.ValueKind == JsonValueKind.Array,
            $"Ожидался массив errors.{field}, фактически: {body.GetRawText()}");

        var actual = fieldErrors.EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.True(
            expectedTexts.SequenceEqual(actual),
            $"Ожидался errors.{field} = [{string.Join(", ", expectedTexts.Select(text => $"'{text}'"))}], " +
            $"фактически [{string.Join(", ", actual.Select(text => $"'{text}'"))}]; полное тело: {body.GetRawText()}");
    }

    /// <summary>Значение числового поля JSON-объекта (int).</summary>
    public static int ReadInt(JsonElement element, string propertyName)
    {
        Assert.True(
            element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number,
            $"Ожидалось числовое поле '{propertyName}', фактически: {element.GetRawText()}");
        return value.GetInt32();
    }

    /// <summary>Значение строкового поля JSON-объекта.</summary>
    public static string ReadString(JsonElement element, string propertyName)
    {
        Assert.True(
            element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String,
            $"Ожидалось строковое поле '{propertyName}', фактически: {element.GetRawText()}");
        return value.GetString()!;
    }

    /// <summary>Значение поля JSON-объекта либо null (поле отсутствует/JSON null; строка "null" не допускается).</summary>
    public static string? ReadStringOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        Assert.True(
            value.ValueKind == JsonValueKind.String,
            $"Ожидалось строковое поле или null в '{propertyName}', фактически: {element.GetRawText()}");
        return value.GetString();
    }

    /// <summary>
    /// id группы по имени из ответа GET /api/v1/groups (переход given «группа
    /// ИК-xxx» к целевому запросу: uuid группы кейсам не известен заранее).
    /// </summary>
    public static async Task<string> GetGroupIdByNameAsync(HttpClient client, string name)
    {
        using var response = await GetGroupsAsync(client);
        using var body = await ParseWithStatusAsync(response, HttpStatusCode.OK, $"GET {GroupsEndpoint} (поиск id «{name}»)");
        Assert.True(
            body.RootElement.ValueKind == JsonValueKind.Array,
            $"Ожидался массив GroupDto в GET {GroupsEndpoint}, фактически: {body.RootElement.GetRawText()}");

        foreach (var group in body.RootElement.EnumerateArray())
        {
            if (ReadString(group, "name") == name)
            {
                return ReadString(group, "id");
            }
        }

        throw new InvalidOperationException(
            $"Группа «{name}» не найдена в GET {GroupsEndpoint} (шаг given неисполним): {body.RootElement.GetRawText()}");
    }

    /// <summary>
    /// Хранилище групп тестового хоста: перечень имён (DI-чтение IGroupRepository,
    /// IF-015) для проверок состояния после отказов (TS-103: «в хранилище ровно
    /// одна группа», TS-104: «G1 не переименована», TS-169: «группа не создана»).
    /// lower-правило уникальности — lower(group.name) FR-024: сравнение
    /// ToLowerInvariant дословно воспроизводит его, не опираясь на код SUT.
    /// </summary>
    public static IReadOnlyList<string> StoredGroupNames(LabsApp.Storage.IGroupRepository groups) =>
        groups.GetAll().Select(group => group.Name).ToList();

    /// <summary>Число групп хранилища с lower(name), равным ожидаемому (FR-024).</summary>
    public static int CountStoredGroupsByCiName(LabsApp.Storage.IGroupRepository groups, string ciName) =>
        groups.GetAll().Count(group => string.Equals(group.Name.ToLowerInvariant(), ciName.ToLowerInvariant(), StringComparison.Ordinal));
}
