using System.Text.Json;

namespace LabsApp.IntegrationTests.B05.Infrastructure;

/// <summary>
/// Проверки тел ответов контракта ошибок (FR-024/NFR-007): точные множества ключей
/// конверта {message, errors?}, дословные message и форма errors {поле: string[]}.
/// Копия механики JsonAssert зоны B-01 (чужая зона недоступна для ссылок).
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
    /// errors содержит ключ поля; значение — непустой массив непустых строк.
    /// При requireCyrillic каждая строка дополнительно содержит хотя бы одну
    /// кириллическую букву («русские тексты» кейса TS-121).
    /// </summary>
    public static void ErrorFieldIsNonEmptyStringArray(JsonElement envelope, string fieldKey, bool requireCyrillic = false)
    {
        Assert.True(
            envelope.TryGetProperty("errors", out var errors),
            "В теле 400 отсутствует ключ errors.");
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);
        Assert.True(
            errors.TryGetProperty(fieldKey, out var fieldErrors),
            $"В errors отсутствует ключ поля «{fieldKey}»; фактические ключи: [{string.Join(", ", errors.EnumerateObject().Select(p => p.Name))}].");
        Assert.Equal(JsonValueKind.Array, fieldErrors.ValueKind);
        var texts = fieldErrors.EnumerateArray().ToList();
        Assert.True(
            texts.Count > 0,
            $"Ожидался непустой массив строк errors.{fieldKey}, фактически пустой массив.");
        foreach (var text in texts)
        {
            Assert.Equal(JsonValueKind.String, text.ValueKind);
            var value = text.GetString();
            Assert.False(
                string.IsNullOrEmpty(value),
                $"Ожидался непустой текст в errors.{fieldKey}, фактически пустая строка.");
            if (requireCyrillic)
            {
                Assert.True(
                    (value ?? string.Empty).Any(IsCyrillic),
                    $"Ожидался русский текст в errors.{fieldKey}, фактически «{value}» без кириллицы.");
            }
        }
    }

    private static bool IsCyrillic(char c) => c is >= '\u0400' and <= '\u04FF';
}
