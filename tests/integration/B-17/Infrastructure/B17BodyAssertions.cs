using System.Text.Json;

namespace LabsApp.IntegrationTests.B17.Infrastructure;

/// <summary>
/// Проверки тел ответов контракта ошибок (IF-001): точные message, форма
/// errors {поле: string[]}, отсутствие errors у не-полевых 400 (IF-013: ветка
/// WRONG_CURRENT_PASSWORD — «400 {'message':'Неверный текущий пароль'} без
/// errors-карты») и поля ProfileDto (IF-013/FR-015). Копия механики BodyAssertions
/// зон B-05/B-15/B-16 (чужие зоны недоступны для ссылок; изоляция зон — BL-001).
/// </summary>
public static class B17BodyAssertions
{
    /// <summary>
    /// Разбирает тело ответа как JSON-объект и возвращает корневой элемент.
    /// </summary>
    public static async Task<JsonElement> ReadRootObjectAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        // Материализация в JsonElement, живущий после dispose документа: клонируем.
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

    /// <summary>В конверте отсутствует ключ errors (не полевая валидация).</summary>
    public static void ErrorsPropertyIsAbsent(JsonElement envelope)
    {
        Assert.False(
            envelope.TryGetProperty("errors", out var errors),
            $"Ожидалось отсутствие ключа errors, фактически: {errors.ToString()}.");
    }

    /// <summary>
    /// errors содержит ключ поля, значение — массив строк, ПОБАЙТОВО равный
    /// ожидаемому составу в том же порядке (словарь ошибок валидации).
    /// </summary>
    public static void ErrorFieldEquals(JsonElement envelope, string fieldKey, params string[] expectedTexts)
    {
        var fieldErrors = GetErrorField(envelope, fieldKey);
        var actual = fieldErrors.EnumerateArray().Select(item =>
        {
            Assert.Equal(JsonValueKind.String, item.ValueKind);
            return item.GetString() ?? string.Empty;
        }).ToList();
        Assert.True(
            expectedTexts.SequenceEqual(actual, StringComparer.Ordinal),
            $"Ожидался errors.{fieldKey} = [{string.Join("; ", expectedTexts)}], фактически [{string.Join("; ", actual)}].");
    }

    /// <summary>
    /// errors содержит ключ поля, массив которого СОДЕРЖИТ каждое из ожидаемых
    /// сообщений (кейс формулирует «содержит», а не точный состав — TS-087).
    /// </summary>
    public static void ErrorFieldContains(JsonElement envelope, string fieldKey, params string[] expectedTexts)
    {
        var fieldErrors = GetErrorField(envelope, fieldKey);
        var actual = fieldErrors.EnumerateArray().Select(item =>
        {
            Assert.Equal(JsonValueKind.String, item.ValueKind);
            return item.GetString() ?? string.Empty;
        }).ToList();
        foreach (var expected in expectedTexts)
        {
            Assert.True(
                actual.Contains(expected, StringComparer.Ordinal),
                $"errors.{fieldKey} должен содержать «{expected}», фактически [{string.Join("; ", actual)}].");
        }
    }

    /// <summary>
    /// Свойство присутствует в объекте и равно JSON-null РОВНО: отсутствие поля
    /// или строковое (в том числе пустое) значение — ошибка (кейс TS-079:
    /// groupName при отсутствии группы — «ровно null, не пустая строка, не
    /// отсутствующее поле»).
    /// </summary>
    public static void PropertyIsNull(JsonElement element, string propertyName)
    {
        Assert.True(
            element.TryGetProperty(propertyName, out var property),
            $"В теле ответа отсутствует ключ «{propertyName}»; фактические ключи: [{string.Join(", ", element.EnumerateObject().Select(p => p.Name))}].");
        Assert.True(
            property.ValueKind == JsonValueKind.Null,
            $"Ожидался JSON-null в «{propertyName}», фактически {property.ValueKind} («{property.ToString()}»).");
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

    private static JsonElement GetErrorField(JsonElement envelope, string fieldKey)
    {
        Assert.True(
            envelope.TryGetProperty("errors", out var errors),
            "В теле 400 отсутствует ключ errors.");
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);
        Assert.True(
            errors.TryGetProperty(fieldKey, out var fieldErrors),
            $"В errors отсутствует ключ поля «{fieldKey}»; фактические ключи: [{string.Join(", ", errors.EnumerateObject().Select(p => p.Name))}].");
        Assert.Equal(JsonValueKind.Array, fieldErrors.ValueKind);
        return fieldErrors;
    }
}
