using System.Text.Json;

namespace LabsApp.IntegrationTests.B15.Infrastructure;

/// <summary>
/// Проверки тел ответов контракта ошибок (IF-001/NFR-007): точные множества ключей
/// конверта {message, errors?}, дословные message и форма errors {поле: string[]}.
/// Копия механики BodyAssertions зон B-05/B-07 (чужие зоны недоступны для ссылок)
/// с точной сверкой состава errors.&lt;поле&gt; (словарь ошибок, TS-092/TS-193/TS-194).
/// </summary>
public static class BodyAssertions
{
    /// <summary>
    /// Разбирает тело ответа как JSON-объект и возвращает корневой элемент.
    /// </summary>
    public static async Task<JsonElement> ReadRootObjectAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        // Materialизация в JsonElement, живущий после dispose документа: клонируем.
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        return root.Clone();
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

    /// <summary>
    /// errors содержит ключ поля, значение — массив строк, ПОБАЙТОВО равный
    /// ожидаемому составу в том же порядке (словарь ошибок валидации).
    /// </summary>
    public static void ErrorFieldEquals(JsonElement envelope, string fieldKey, params string[] expectedTexts)
    {
        Assert.True(
            envelope.TryGetProperty("errors", out var errors),
            "В теле 400 отсутствует ключ errors.");
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);
        Assert.True(
            errors.TryGetProperty(fieldKey, out var fieldErrors),
            $"В errors отсутствует ключ поля «{fieldKey}»; фактические ключи: [{string.Join(", ", errors.EnumerateObject().Select(p => p.Name))}].");
        Assert.Equal(JsonValueKind.Array, fieldErrors.ValueKind);
        var actual = fieldErrors.EnumerateArray().Select(item =>
        {
            Assert.Equal(JsonValueKind.String, item.ValueKind);
            return item.GetString() ?? string.Empty;
        }).ToList();
        Assert.True(
            expectedTexts.SequenceEqual(actual, StringComparer.Ordinal),
            $"Ожидался errors.{fieldKey} = [{string.Join("; ", expectedTexts)}], фактически [{string.Join("; ", actual)}].");
    }

    /// <summary>Строковое свойство JSON-объекта равно ожидаемому дословно.</summary>
    public static void StringPropertyIs(JsonElement element, string propertyName, string expected)
    {
        Assert.True(
            element.TryGetProperty(propertyName, out var property),
            $"В теле ответа отсутствует ключ «{propertyName}»; фактические ключи: [{string.Join(", ", element.EnumerateObject().Select(p => p.Name))}].");
        Assert.Equal(JsonValueKind.String, property.ValueKind);
        Assert.True(
            string.Equals(property.GetString(), expected, StringComparison.Ordinal),
            $"Ожидался {propertyName} = «{expected}» дословно, фактически «{property.GetString()}».");
    }
}
