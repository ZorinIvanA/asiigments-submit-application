using System.Text.Json;

namespace LabsApp.IntegrationTests.B05.Infrastructure;

/// <summary>
/// Дополнительные проверки тел ответов для кейсов FR-020/FR-021 поверх
/// <see cref="BodyAssertions"/>: точное содержимое массива полевой ошибки.
/// </summary>
public static class B05ContractAsserts
{
    /// <summary>
    /// errors.{fieldKey} — массив РОВНО из одного элемента с дословным текстом
    /// (кейсы «errors.search=[...]» / «errors.semester=[...]» без лишних сообщений).
    /// </summary>
    public static void SingleFieldErrorIs(JsonElement envelope, string fieldKey, string expectedText)
    {
        BodyAssertions.ErrorFieldIsNonEmptyStringArray(envelope, fieldKey);
        var actual = envelope.GetProperty("errors").GetProperty(fieldKey)
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToList();
        Assert.True(
            actual.Count == 1 && string.Equals(actual[0], expectedText, StringComparison.Ordinal),
            $"Ожидался errors.{fieldKey} ровно [\"{expectedText}\"], фактически [{string.Join("; ", actual)}].");
    }
}
