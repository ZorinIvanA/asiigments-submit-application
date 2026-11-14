namespace LabsApp.IntegrationTests.B09.Infrastructure;

/// <summary>
/// Проверки ответов по контракту ошибок (IF-001/NFR-007): точные статусы, дословные
/// message, форма errors {поле: string[]} и вспомогательные чтения свойств JSON.
/// Копия механики BodyAssertions зон B-05/B-07 (чужие зоны недоступны для ссылок).
/// </summary>
public static class B09Assertions
{
    /// <summary>
    /// Проверяет статус и разбирает тело ответа как JSON-объект. Несовпадение статуса
    /// или не-JSON-объект — падение с полным фактическим телом в сообщении.
    /// </summary>
    public static async Task<JsonElement> ParseObjectAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string context)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == expectedStatus,
            $"{context}: ожидался статус {(int)expectedStatus}, фактически {(int)response.StatusCode}. Тело: {raw}");

        try
        {
            using var document = JsonDocument.Parse(raw);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            Assert.Fail(
                $"{context}: тело ответа не является корректным JSON-объектом ({exception.Message}). Тело: {raw}");
            return default;
        }
    }

    /// <summary>Поле message объекта-конверта равно ожидаемой строке дословно.</summary>
    public static void MessageIs(JsonElement envelope, string expected)
    {
        Assert.True(
            envelope.TryGetProperty("message", out var message),
            "В теле ошибки отсутствует ключ message.");
        Assert.Equal(JsonValueKind.String, message.ValueKind);
        Assert.True(
            string.Equals(message.GetString(), expected, StringComparison.Ordinal),
            $"Ожидался message «{expected}» дословно, фактически «{message.GetString()}».");
    }

    /// <summary>Тело ответа пусто (TS-081: «200 с пустым телом»).</summary>
    public static async Task BodyIsEmptyAsync(HttpResponseMessage response, string context)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(
            string.IsNullOrEmpty(raw),
            $"{context}: ожидалось пустое тело, фактически: {raw}");
    }
}
