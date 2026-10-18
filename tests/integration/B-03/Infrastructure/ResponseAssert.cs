namespace LabsApp.IntegrationTests.B03.Infrastructure;

/// <summary>
/// Проверки HTTP-ответов батча B-03: статус с диагностикой тела, точные тексты
/// message (словарь текстов ошибок) и состав errors.&lt;поле&gt; в 400/409-ответах
/// (FR-006: «все ошибки собираются одновременно», тексты — дословно).
/// </summary>
public static class ResponseAssert
{
    /// <summary>
    /// Проверяет статус и парсит JSON-тело. При несовпадении статуса сообщение
    /// включает фактическое тело (диагностика «expected/actual» для баг-репортов).
    /// </summary>
    public static async Task<JsonDocument> ParseWithStatusAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string step)
    {
        Assert.True(
            response.StatusCode == expected,
            $"Ожидался HTTP {(int)expected} на шаге «{step}», фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    /// <summary>Проверяет точное равенство body.message ожидаемому тексту.</summary>
    public static void MessageIs(JsonElement body, string expected) =>
        Assert.True(
            body.TryGetProperty("message", out var message) && message.GetString() == expected,
            $"Ожидался message «{expected}», фактически: {body.GetRawText()}");

    /// <summary>
    /// errors.&lt;field&gt; — точное совпадение ПОЛНОГО перечня текстов в заданном
    /// порядке: порядок и сами тексты — дословно ожидаемые, посторонних нет.
    /// </summary>
    public static void FieldErrorsExactly(JsonElement body, string field, params string[] expectedTexts)
    {
        Assert.True(
            body.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object,
            $"Ожидался объект errors в теле ответа, фактически: {body.GetRawText()}");
        Assert.True(
            errors.TryGetProperty(field, out var fieldErrors) && fieldErrors.ValueKind == JsonValueKind.Array,
            $"Ожидался массив errors.{field}, фактически: {body.GetRawText()}");

        var actual = fieldErrors.EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.True(
            expectedTexts.SequenceEqual(actual),
            $"Ожидался errors.{field} = [{string.Join(", ", expectedTexts.Select(text => $"'{text}'"))}], " +
            $"фактически [{string.Join(", ", actual.Select(text => $"'{text}'"))}]; полное тело: {body.GetRawText()}");
    }

    /// <summary>
    /// errors.&lt;field&gt; — тот же ПОЛНЫЙ перечень текстов без требования к порядку
    /// (кейс TS-032: «errors.password содержит ровно 3 текста» — состав фиксирован,
    /// порядок словарём не нормирован).
    /// </summary>
    public static void FieldErrorsSameElements(JsonElement body, string field, params string[] expectedTexts)
    {
        Assert.True(
            body.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object,
            $"Ожидался объект errors в теле ответа, фактически: {body.GetRawText()}");
        Assert.True(
            errors.TryGetProperty(field, out var fieldErrors) && fieldErrors.ValueKind == JsonValueKind.Array,
            $"Ожидался массив errors.{field}, фактически: {body.GetRawText()}");

        var actual = fieldErrors.EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.True(
            expectedTexts.Length == actual.Length
            && !expectedTexts.Except(actual).Any()
            && !actual.Except(expectedTexts).Any(),
            $"Ожидался errors.{field} — ровно [{string.Join(", ", expectedTexts.Select(text => $"'{text}'"))}], " +
            $"фактически [{string.Join(", ", actual.Select(text => $"'{text}'"))}]; полное тело: {body.GetRawText()}");
    }
}
