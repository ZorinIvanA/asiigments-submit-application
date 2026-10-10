using System.Text.Json;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Проверки тел ответов контракта ошибок (FR-011/FR-012/IF-001): точные множества
/// ключей конверта {message, errors?}, дословные message и форма errors {поле: string[]}.
/// Копия механики BodyAssertions зоны B-05 (чужая зона недоступна для ссылок).
/// </summary>
public static class BodyAssertions
{
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

    /// <summary>Разбирает тело ответа как JSON-объект и возвращает корневой элемент.</summary>
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

    /// <summary>errors содержит ключ поля, и массив его текстов СОДЕРЖИТ ожидаемую строку дословно.</summary>
    public static void ErrorFieldContains(JsonElement envelope, string fieldKey, string expectedText)
    {
        var texts = GetErrorFieldTexts(envelope, fieldKey);
        Assert.True(
            texts.Any(text => string.Equals(text, expectedText, StringComparison.Ordinal)),
            $"Ожидалось, что errors.{fieldKey} содержит «{expectedText}»; фактически: [{string.Join("; ", texts)}].");
    }

    /// <summary>Массив текстов errors.{fieldKey} ПОЭЛЕМЕНТНО равен ожидаемому (порядок и длина).</summary>
    public static void ErrorFieldIsExactly(JsonElement envelope, string fieldKey, params string[] expectedTexts)
    {
        var texts = GetErrorFieldTexts(envelope, fieldKey);
        Assert.True(
            expectedTexts.SequenceEqual(texts, StringComparer.Ordinal),
            $"Ожидались errors.{fieldKey} = [{string.Join("; ", expectedTexts)}]; фактически: [{string.Join("; ", texts)}].");
    }

    private static List<string> GetErrorFieldTexts(JsonElement envelope, string fieldKey)
    {
        Assert.True(
            envelope.TryGetProperty("errors", out var errors),
            "В теле 400 отсутствует ключ errors.");
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);
        Assert.True(
            errors.TryGetProperty(fieldKey, out var fieldErrors),
            $"В errors отсутствует ключ поля «{fieldKey}»; фактические ключи: [{string.Join(", ", errors.EnumerateObject().Select(p => p.Name))}].");
        Assert.Equal(JsonValueKind.Array, fieldErrors.ValueKind);

        var texts = new List<string>();
        foreach (var item in fieldErrors.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.String, item.ValueKind);
            var value = item.GetString();
            Assert.False(
                string.IsNullOrEmpty(value),
                $"Ожидался непустой текст в errors.{fieldKey}, фактически пустая строка.");
            texts.Add(value!);
        }

        return texts;
    }
}
