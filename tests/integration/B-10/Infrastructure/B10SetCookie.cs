namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>
/// Разбор одного заголовка Set-Cookie (кейсы TS-047/TS-191/TS-192, матрица
/// NFR-007): первая секция — имя=значение, остальные — атрибуты. Имена атрибутов
/// сравниваются без учёта регистра (Kestrel пишет их в нижнем регистре), флаги
/// без значения (httponly, secure) фиксируются присутствием.
/// </summary>
public sealed class B10SetCookie
{
    private readonly Dictionary<string, string?> _attributes;

    private B10SetCookie(string name, string value, Dictionary<string, string?> attributes)
    {
        Name = name;
        Value = value;
        _attributes = attributes;
    }

    public string Name { get; }

    public string Value { get; }

    /// <summary>Атрибут присутствует в заголовке (для флагов HttpOnly/Secure).</summary>
    public bool HasFlag(string attributeName) => _attributes.ContainsKey(attributeName);

    /// <summary>Значение атрибута либо null, если атрибут отсутствует.</summary>
    public string? Attribute(string attributeName) =>
        _attributes.TryGetValue(attributeName, out var value) ? value : null;

    public static B10SetCookie Parse(string header)
    {
        var sections = header.Split(';');
        var nameValue = sections[0].Split('=', 2);
        var attributes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections.Skip(1))
        {
            var pair = section.Split('=', 2);
            attributes[pair[0].Trim()] = pair.Length > 1 ? pair[1].Trim() : null;
        }

        return new B10SetCookie(
            nameValue[0].Trim(),
            nameValue.Length > 1 ? nameValue[1].Trim() : string.Empty,
            attributes);
    }
}

/// <summary>Извлечение всех Set-Cookie заголовков ответа (кейсы TS-047/TS-191/TS-192).</summary>
public static class B10SetCookieReader
{
    public static IReadOnlyList<B10SetCookie> Read(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return Array.Empty<B10SetCookie>();
        }

        return values.Select(B10SetCookie.Parse).ToList();
    }
}
