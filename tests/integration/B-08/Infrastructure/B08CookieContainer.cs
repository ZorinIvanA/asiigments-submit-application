using System.Globalization;

namespace LabsApp.IntegrationTests.B08.Infrastructure;

/// <summary>
/// Ручной cookie-контейнер для сквозных сценариев B-08: тест сам переносит cookie
/// из ответов (заголовок Set-Cookie) в последующие запросы (заголовок Cookie) —
/// без автоматического CookieContainer в HttpClientHandler, чтобы сервер видел
/// ровно те cookie, которые сценарий решил отправить (кейсы TS-061..TS-065:
/// состав cookie — часть given/when). Копия механики TestCookieContainer зоны
/// src/api/LabsApp.Tests (чужая зона недоступна для ссылок; изоляция зон —
/// BL-001 BUG-001). CaptureFrom: значение cookie перезаписывается; удаление
/// (пустое значение или атрибут Max-Age&lt;=0) убирает cookie. ApplyTo: пары
/// контейнера имеют приоритет над существующим заголовком запроса, посторонние
/// пары (не из контейнера) сохраняются. Не потокобезопасен — сценарии
/// последовательны.
/// </summary>
public sealed class B08CookieContainer
{
    private const string SetCookieHeaderName = "Set-Cookie";
    private const string CookieHeaderName = "Cookie";
    private const string MaxAgeAttributeName = "max-age";

    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    /// <summary>Количество cookie в контейнере.</summary>
    public int Count => _cookies.Count;

    /// <summary>Переносит cookie из Set-Cookie ответа в контейнер (перезапись/удаление).</summary>
    public void CaptureFrom(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.Headers.TryGetValues(SetCookieHeaderName, out var setCookies))
        {
            return;
        }

        foreach (var setCookie in setCookies)
        {
            CaptureFromSetCookie(setCookie);
        }
    }

    /// <summary>Выставляет заголовок Cookie запроса из содержимого контейнера.</summary>
    public void ApplyTo(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.Headers.TryGetValues(CookieHeaderName, out var existingHeaders))
        {
            foreach (var headerValue in existingHeaders)
            foreach (var pair in headerValue.Split(
                         ';',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = pair.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                pairs[pair[..separator]] = pair[(separator + 1)..];
            }
        }

        foreach (var (name, value) in _cookies)
        {
            pairs[name] = value;
        }

        if (pairs.Count == 0)
        {
            return;
        }

        request.Headers.Remove(CookieHeaderName);
        request.Headers.TryAddWithoutValidation(
            CookieHeaderName,
            string.Join("; ", pairs.Select(static pair => $"{pair.Key}={pair.Value}")));
    }

    /// <summary>Значение cookie по имени; null — cookie нет.</summary>
    public string? GetValue(string name) => _cookies.TryGetValue(name, out var value) ? value : null;

    /// <summary>Есть ли cookie с именем.</summary>
    public bool Contains(string name) => _cookies.ContainsKey(name);

    /// <summary>Ручная установка/перезапись cookie.</summary>
    public void Set(string name, string value) => _cookies[name] = value;

    /// <summary>Удаляет cookie; true — была и удалена.</summary>
    public bool Remove(string name) => _cookies.Remove(name);

    /// <summary>Полная очистка контейнера.</summary>
    public void Clear() => _cookies.Clear();

    private void CaptureFromSetCookie(string setCookie)
    {
        var nameValue = setCookie.Split(';', 2)[0];
        var separator = nameValue.IndexOf('=');
        if (separator <= 0)
        {
            return;
        }

        var name = nameValue[..separator].Trim();
        var value = nameValue[(separator + 1)..].Trim();
        if (value.Length == 0 || IsExpired(setCookie))
        {
            _cookies.Remove(name);
        }
        else
        {
            _cookies[name] = value;
        }
    }

    private static bool IsExpired(string setCookie)
    {
        foreach (var attribute in setCookie.Split(';').Skip(1))
        {
            var parts = attribute.Split('=', 2);
            if (!parts[0].Trim().Equals(MaxAgeAttributeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = parts.Length > 1 ? parts[1].Trim() : null;
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxAge)
                && maxAge <= 0)
            {
                return true;
            }
        }

        return false;
    }
}
