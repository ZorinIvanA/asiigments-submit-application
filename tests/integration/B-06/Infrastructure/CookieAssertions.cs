using LabsApp.Auth;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Инспекция заголовков Set-Cookie (TS-029/TS-030 — FR-011 AC «Флаги cookie», NFR-010):
/// разбор атрибутов каждой cookie (Path, HttpOnly, SameSite, Secure) и сравнение
/// с контрактом IF-004. Атрибуты регистронезависимы, порядок не важен.
/// </summary>
public static class CookieAssertions
{
    public sealed record SetCookie(string Name, string Value, string? Path, bool HttpOnly, bool Secure, string? SameSite);

    /// <summary>Разбирает ВСЕ заголовки Set-Cookie ответа.</summary>
    public static IReadOnlyList<SetCookie> ParseAll(HttpResponseMessage response)
    {
        var result = new List<SetCookie>();
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var raw in values)
            {
                if (raw is not null)
                {
                    result.Add(Parse(raw));
                }
            }
        }

        return result;
    }

    /// <summary>Находит Set-Cookie с именем кейса; отсутствие — ошибка с фактическим списком.</summary>
    public static SetCookie RequireCookie(HttpResponseMessage response, string name)
    {
        var all = ParseAll(response);
        var cookie = all.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        Assert.True(
            cookie is not null,
            $"В ответе отсутствует Set-Cookie «{name}»; фактически: [{string.Join(" | ", all.Select(item => item.Name))}].");
        return cookie!;
    }

    /// <summary>
    /// Контракт auth-cookie IF-004/NFR-010: HttpOnly, SameSite=Strict, Path, и
    /// Secure — только в Production (<paramref name="expectSecure"/>).
    /// </summary>
    public static void AssertAuthCookie(HttpResponseMessage response, string name, string expectedPath, bool expectSecure)
    {
        var cookie = RequireCookie(response, name);
        Assert.True(
            cookie.HttpOnly,
            $"Cookie {name}: ожидался флаг HttpOnly, фактически отсутствует (Set-Cookie: {cookie}).");
        Assert.True(
            string.Equals(cookie.SameSite, "strict", StringComparison.OrdinalIgnoreCase),
            $"Cookie {name}: ожидался SameSite=strict, фактически «{cookie.SameSite}».");
        Assert.True(
            string.Equals(cookie.Path, expectedPath, StringComparison.Ordinal),
            $"Cookie {name}: ожидался Path={expectedPath}, фактически «{cookie.Path}».");
        Assert.True(
            cookie.Secure == expectSecure,
            $"Cookie {name}: ожидался Secure={(expectSecure ? "присутствует" : "отсутствует")}, фактически {(cookie.Secure ? "присутствует" : "отсутствует")}.");
    }

    /// <summary>
    /// Неявного обновления сессии нет (TS-031, FR-011 AC «Просроченный access»):
    /// ни один Set-Cookie ответа не устанавливает непустую access_token/refresh_token.
    /// </summary>
    public static void AssertNoNewAuthCookie(HttpResponseMessage response)
    {
        foreach (var cookie in ParseAll(response))
        {
            var isAuthCookie = cookie.Name is AuthCoreDefaults.AccessTokenCookieName
                or AuthCoreDefaults.RefreshTokenCookieName;
            Assert.False(
                isAuthCookie && !string.IsNullOrEmpty(cookie.Value),
                $"Ответ неявно обновил сессию: Set-Cookie устанавливает непустую {cookie.Name}.");
        }
    }

    private static SetCookie Parse(string raw)
    {
        var segments = raw.Split(';');
        var nameValue = segments[0].Trim();
        var separator = nameValue.IndexOf('=');
        var name = separator < 0 ? nameValue : nameValue[..separator].Trim();
        var value = separator < 0 ? string.Empty : nameValue[(separator + 1)..].Trim();

        string? path = null;
        string? sameSite = null;
        var httpOnly = false;
        var secure = false;
        foreach (var attribute in segments.Skip(1))
        {
            var trimmed = attribute.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var attrSeparator = trimmed.IndexOf('=');
            var attrName = attrSeparator < 0 ? trimmed : trimmed[..attrSeparator].Trim();
            var attrValue = attrSeparator < 0 ? null : trimmed[(attrSeparator + 1)..].Trim();
            if (attrName.Equals("path", StringComparison.OrdinalIgnoreCase))
            {
                path = attrValue;
            }
            else if (attrName.Equals("samesite", StringComparison.OrdinalIgnoreCase))
            {
                sameSite = attrValue;
            }
            else if (attrName.Equals("httponly", StringComparison.OrdinalIgnoreCase))
            {
                httpOnly = true;
            }
            else if (attrName.Equals("secure", StringComparison.OrdinalIgnoreCase))
            {
                secure = true;
            }
        }

        return new SetCookie(name, value, path, httpOnly, secure, sameSite);
    }
}
