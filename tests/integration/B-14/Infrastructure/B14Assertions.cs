using System.Text.Json;

namespace LabsApp.IntegrationTests.B14.Infrastructure;

/// <summary>
/// Проверки тел ответов (IF-001): чтение JSON-тел, дословные строковые свойства
/// и JSON-null свойства (TS-196: groupName=null). Копия механики BodyAssertions
/// зон B-05/B-15 (чужие зоны недоступны для ссылок; изоляция зон — BL-001 BUG-001).
/// </summary>
public static class B14Assertions
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

    /// <summary>Свойство JSON-объекта присутствует и равно литералу null (TS-196).</summary>
    public static void PropertyIsNull(JsonElement element, string propertyName)
    {
        Assert.True(
            element.TryGetProperty(propertyName, out var property),
            $"В теле ответа отсутствует ключ «{propertyName}»; фактические ключи: [{string.Join(", ", element.EnumerateObject().Select(p => p.Name))}].");
        Assert.True(
            property.ValueKind == JsonValueKind.Null,
            $"Ожидался {propertyName} = null, фактически {property.ToString()}.");
    }
}
