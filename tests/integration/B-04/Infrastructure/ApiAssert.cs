namespace LabsApp.IntegrationTests.B04.Infrastructure;

/// <summary>
/// Проверки единого контракта ошибок и чтение JSON-тел (копия механики зоны B-01:
/// фабрика/помощники чужих зон не переиспользуются — BL-001 BUG-001).
/// </summary>
public static class ApiAssert
{
    /// <summary>Разобранное корневое JsonElement тела ответа (клон — документ освобождается).</summary>
    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>HTTP 200 + application/json + разобранное тело.</summary>
    public static async Task<JsonElement> ReadOkJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return await ReadJsonAsync(response);
    }

    /// <summary>
    /// then-проверка ошибочного ответа: точный статус, application/json и дословный
    /// message-текст. С exactSingleMessageProperty — тело ровно {message} (конверт без errors).
    /// </summary>
    public static async Task<JsonElement> AssertMessageAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedMessage,
        bool exactSingleMessageProperty = false)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var root = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("message", out var message), "Тело ошибки без свойства message.");
        Assert.Equal(expectedMessage, message.GetString());

        if (exactSingleMessageProperty)
        {
            HasExactlyProperties(root, "message");
        }

        return root;
    }

    /// <summary>Множество имён свойств объекта ровно равно ожидаемому.</summary>
    public static void HasExactlyProperties(JsonElement element, params string[] expectedNames)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        var actualNames = element.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal);
        var expected = expectedNames.OrderBy(name => name, StringComparer.Ordinal);
        Assert.True(
            expected.SequenceEqual(actualNames, StringComparer.Ordinal),
            $"Ожидались ключи [{string.Join(", ", expectedNames)}], фактически: [{string.Join(", ", actualNames)}].");
    }

    /// <summary>
    /// then-проверка полевой ошибки 400: errors.{field} — массив РОВНО из одного
    /// дословного текста (кейсы задают ошибки одной формой errors.{field}=['…']).
    /// </summary>
    public static async Task AssertSingleFieldErrorAsync(
        HttpResponseMessage response,
        string field,
        string expectedText)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var root = await ReadJsonAsync(response);
        HasNonEmptyError(root, field);

        var fieldErrors = root.GetProperty("errors").GetProperty(field);
        Assert.Equal(1, fieldErrors.GetArrayLength());
        Assert.Equal(expectedText, fieldErrors[0].GetString());
    }

    /// <summary>Тело 400 содержит errors.{field} — непустой массив текстов.</summary>
    public static void HasNonEmptyError(JsonElement root, string field)
    {
        Assert.True(root.TryGetProperty("errors", out var errors), "Тело 400 без свойства errors.");
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);
        Assert.True(
            errors.TryGetProperty(field, out var fieldErrors),
            $"errors не содержит поле «{field}»; фактические ключи: [{string.Join(", ", errors.EnumerateObject().Select(property => property.Name))}].");
        Assert.True(
            fieldErrors.ValueKind == JsonValueKind.Array && fieldErrors.GetArrayLength() > 0,
            $"errors.{field} должен быть непустым массивом текстов.");
    }
}
