using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LabsApp.IntegrationTests.B08.Infrastructure;

/// <summary>
/// Проверки ответов кейсов B-08: очистка cookie через Set-Cookie Max-Age=0
/// (FR-015: «обе Set-Cookie с Max-Age=0»), пустое тело-объект {}
/// (FR-017: «200 с пустым телом-объектом») и шаблон 6 ASCII-цифр
/// («строкой из ровно 6 ASCII-цифр, проверяется шаблоном»).
/// </summary>
public static partial class ResponseAssertions
{
    [GeneratedRegex("(?<![0-9])[0-9]{6}(?![0-9])")]
    private static partial Regex SixAsciiDigits();

    private const string SetCookieHeaderName = "Set-Cookie";
    private const string MaxAgeAttributeName = "max-age";

    /// <summary>
    /// В ответе есть Set-Cookie для cookie с данным именем с атрибутом Max-Age=0
    /// (механизм очистки cookie на клиенте, FR-015/IF-004: ClearAuth — Max-Age=0
    /// с теми же атрибутами). Сверка имени и атрибута без учёта регистра.
    /// </summary>
    public static void AssertCookieClearedByMaxAgeZero(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues(SetCookieHeaderName, out var setCookies))
        {
            throw new Xunit.Sdk.XunitException(
                $"В ответе нет заголовков Set-Cookie: очистка cookie «{cookieName}» не зафиксирована (FR-015).");
        }

        foreach (var setCookie in setCookies)
        {
            var parts = setCookie.Split(';', 2);
            var nameValue = parts[0];
            var separator = nameValue.IndexOf('=');
            if (separator <= 0
                || !string.Equals(nameValue[..separator].Trim(), cookieName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (HasMaxAgeZero(parts.Length > 1 ? parts[1] : string.Empty))
            {
                return;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Set-Cookie для «{cookieName}» с Max-Age=0 не найдено; фактические Set-Cookie: [{string.Join(" | ", setCookies)}].");
    }

    private static bool HasMaxAgeZero(string attributes)
    {
        foreach (var attribute in attributes.Split(';'))
        {
            var parts = attribute.Split('=', 2);
            if (!parts[0].Trim().Equals(MaxAgeAttributeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = parts.Length > 1 ? parts[1].Trim() : null;
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxAge)
                && maxAge == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Тело — пустой JSON-объект {} без свойств (FR-017: «пустым телом-объектом»).</summary>
    public static void AssertEmptyJsonObject(string content, string context)
    {
        using var document = ParseJson(content, context);
        var root = document.RootElement;
        Assert.True(
            root.ValueKind == JsonValueKind.Object,
            $"{context}: ожидалось тело-объект, фактически ValueKind={root.ValueKind}: «{content}».");
        var properties = root.EnumerateObject().ToList();
        Assert.True(
            properties.Count == 0,
            $"{context}: ожидалось пустое тело-объект {{}}, фактически свойства: [{string.Join(", ", properties.Select(p => p.Name))}].");
    }

    /// <summary>
    /// Тело — ПУСТО (0 байт): recovery/request отвечает 200 без контента
    /// (ADR-012, ISS-014 — НЕ «{}»).
    /// </summary>
    public static void AssertEmptyBody(string content, string context)
    {
        Assert.True(
            content.Length == 0,
            $"{context}: ожидалось пустое тело (0 байт — ADR-012/ISS-014), "
            + $"фактически {content.Length} байт: «{content}».");
    }

    /// <summary>message конверта равен ожидаемой строке дословно (IF-001/NFR-007).</summary>
    public static void AssertMessageEquals(string content, string expected, string context)
    {
        using var document = ParseJson(content, context);
        var root = document.RootElement;
        Assert.True(
            root.ValueKind == JsonValueKind.Object,
            $"{context}: ожидался JSON-объект, фактически ValueKind={root.ValueKind}: «{content}».");
        Assert.True(
            root.TryGetProperty("message", out var message),
            $"{context}: в теле отсутствует ключ message: «{content}».");
        Assert.True(
            message.ValueKind == JsonValueKind.String
            && string.Equals(message.GetString(), expected, StringComparison.Ordinal),
            $"{context}: ожидался message «{expected}» дословно, фактически «{message.GetString()}».");
    }

    /// <summary>Строковое свойство JSON-объекта равно ожидаемому дословно (MeDto, IF-007).</summary>
    public static void AssertStringPropertyIs(System.Text.Json.JsonElement body, string propertyName, string expected)
    {
        Assert.True(
            body.TryGetProperty(propertyName, out var property),
            $"В теле ответа отсутствует ключ «{propertyName}»; фактические ключи: [{string.Join(", ", body.EnumerateObject().Select(p => p.Name))}].");
        Assert.True(
            property.ValueKind == JsonValueKind.String
            && string.Equals(property.GetString(), expected, StringComparison.Ordinal),
            $"Ожидалось {propertyName} = «{expected}» дословно, фактически «{property.GetString()}».");
    }

    /// <summary>
    /// Свойство JSON-объекта присутствует и равно JSON-null
    /// (FR-016: «groupName=null (висячей ссылки нет)», null в JSON присутствует всегда).
    /// </summary>
    public static void AssertNullProperty(System.Text.Json.JsonElement body, string propertyName)
    {
        Assert.True(
            body.TryGetProperty(propertyName, out var property),
            $"В теле ответа отсутствует ключ «{propertyName}»; фактические ключи: [{string.Join(", ", body.EnumerateObject().Select(p => p.Name))}].");
        Assert.True(
            property.ValueKind == JsonValueKind.Null,
            $"Ожидалось {propertyName} = null, фактически ValueKind={property.ValueKind}.");
    }

    /// <summary>Сериализованная запись журнала содержит строку из ровно 6 ASCII-цифр (шаблон, значение не фиксируется).</summary>
    public static bool HasSixDigitRun(B08LogRecord record) =>
        SixAsciiDigits().IsMatch(record.Serialize());

    /// <summary>Извлекает строку из ровно 6 ASCII-цифр из сериализованной записи журнала; null — совпадений нет.</summary>
    public static string? ExtractSixDigitRun(B08LogRecord record)
    {
        var match = SixAsciiDigits().Match(record.Serialize());
        return match.Success ? match.Value : null;
    }

    private static JsonDocument ParseJson(string content, string context)
    {
        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new Xunit.Sdk.XunitException(
                $"{context}: тело ответа не является JSON: «{content}» ({exception.Message}).");
        }
    }
}
