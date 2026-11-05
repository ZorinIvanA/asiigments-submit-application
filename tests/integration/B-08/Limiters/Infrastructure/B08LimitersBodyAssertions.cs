using System.Text.Json;

namespace LabsApp.IntegrationTests.B08.Limiters.Infrastructure;

/// <summary>
/// Проверки тел ответов контракта ошибок (конверт {message, errors?}): разбор
/// корневого JSON-объекта и дословная сверка message. Копия механики
/// BodyAssertions зоны B-07 (чужая зона недоступна для ссылок; изоляция зон —
/// BL-001 BUG-001), сокращённая до потребностей кейсов TS-019..TS-022, TS-024.
/// </summary>
public static class B08LimitersBodyAssertions
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
}
