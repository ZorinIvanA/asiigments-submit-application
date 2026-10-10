namespace LabsApp.IntegrationTests.B01.Infrastructure;

/// <summary>
/// JSON-проверки контрактов (TS-002 и др.): Content-Type, точные множества ключей
/// и тексты единого конверта ошибок (FR-023).
/// </summary>
public static class JsonAssert
{
    /// <summary>
    /// Тело ответа — единый конверт ошибки (FR-023): Content-Type application/json,
    /// ровно один ключ message со строковым значением, текст — ожидаемый. Ошибки
    /// конвейера приложения не несут ключа errors (он допустим только у 400 полевой
    /// валидации). Возвращает сырое тело — для дополнительных проверок
    /// («тело НЕ содержимое index.html» и т.п.).
    /// </summary>
    public static async Task<string> ErrorEnvelopeAsync(HttpResponseMessage response, string expectedMessage)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        HasExactlyProperties(document.RootElement, "message");
        Assert.Equal(expectedMessage, document.RootElement.GetProperty("message").GetString());
        return body;
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
