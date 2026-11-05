namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>
/// Проверки единого контракта ошибок и чтение JSON-тел (копия механики зоны B-04:
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
        AssertStatus(response, expectedStatus, $"Ожидался {(int)expectedStatus}");
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

    /// <summary>
    /// then-проверка статуса без требований к телу (например, TS-117: «400 раньше 404»):
    /// точный статус + application/json (IF-001: все ошибки /api — JSON-конверт).
    /// </summary>
    public static void AssertStatus(HttpResponseMessage response, HttpStatusCode expectedStatus, string context)
    {
        Assert.True(
            response.StatusCode == expectedStatus,
            $"{context}: ожидался {(int)expectedStatus}, фактически {(int)response.StatusCode}.");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
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
}
