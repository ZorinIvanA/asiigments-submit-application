using LabsApp.Auth;
using System.Globalization;
using System.Text;

namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>Шаг auth-потока: эндпойнт, статус и разобранные Set-Cookie ответа.</summary>
public sealed record B10CookieFlowStep(
    string Endpoint,
    HttpStatusCode Status,
    IReadOnlyList<B10SetCookie> Cookies);

/// <summary>
/// Полный auth-поток матрицы NFR-007 (кейсы TS-191/TS-192): register → login →
/// refresh → logout с разбором КАЖДОГО заголовка Set-Cookie. Клиент передаётся
/// БЕЗ cookie-контейнера (HandleCookies=false): refresh/logout несут cookie
/// явным заголовком Cookie из значений шага login (ротации refresh нет — ТЗ п.4,
/// поэтому значение login живо до logout). Статусы шагов не проверяются здесь —
/// кейсы проверяют матрицу cookie; успех шагов кейс подтверждает сам.
/// </summary>
public static class B10CookieFlow
{
    /// <summary>Эндпойнт регистрации (версионированный префикс FR-001).</summary>
    public const string RegisterPath = "/api/v1/auth/register";

    /// <summary>Эндпойнт входа (кейсы TS-047/TS-048/TS-049/TS-051).</summary>
    public const string LoginPath = "/api/v1/auth/login";

    /// <summary>Эндпойнт обновления access-токена.</summary>
    public const string RefreshPath = "/api/v1/auth/refresh";

    /// <summary>Эндпойнт выхода.</summary>
    public const string LogoutPath = "/api/v1/auth/logout";

    public static async Task<IReadOnlyList<B10CookieFlowStep>> RunAsync(
        HttpClient client,
        string login,
        string password,
        string fullName)
    {
        // register: каноническая форма FR-006 — 2 Set-Cookie (access + refresh).
        using var registerResponse = await client.PostAsJsonAsync(
            RegisterPath,
            new
            {
                fullName,
                login,
                email = string.Create(CultureInfo.InvariantCulture, $"{login}@example.com"),
                password,
                repeatPassword = password,
            });
        var registerCookies = B10SetCookieReader.Read(registerResponse);

        // login: 2 Set-Cookie; значения — носители шагов refresh/logout.
        using var loginResponse = await client.PostAsJsonAsync(LoginPath, new { login, password });
        var loginCookies = B10SetCookieReader.Read(loginResponse);
        var loginAccess = RequireCookie(loginCookies, AuthCoreDefaults.AccessTokenCookieName, LoginPath).Value;
        var loginRefresh = RequireCookie(loginCookies, AuthCoreDefaults.RefreshTokenCookieName, LoginPath).Value;

        // refresh: ТОЛЬКО access_token (refresh не переустанавливается — NFR-007).
        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, RefreshPath);
        refreshRequest.Headers.TryAddWithoutValidation(
            "Cookie",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{AuthCoreDefaults.RefreshTokenCookieName}={loginRefresh}"));
        using var refreshResponse = await client.SendAsync(refreshRequest);
        var refreshCookies = B10SetCookieReader.Read(refreshResponse);

        // logout: оба cookie с Max-Age=0 (сброс).
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, LogoutPath);
        logoutRequest.Headers.TryAddWithoutValidation(
            "Cookie",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{AuthCoreDefaults.AccessTokenCookieName}={loginAccess}"));
        using var logoutResponse = await client.SendAsync(logoutRequest);
        var logoutCookies = B10SetCookieReader.Read(logoutResponse);

        return
        [
            new B10CookieFlowStep(RegisterPath, registerResponse.StatusCode, registerCookies),
            new B10CookieFlowStep(LoginPath, loginResponse.StatusCode, loginCookies),
            new B10CookieFlowStep(RefreshPath, refreshResponse.StatusCode, refreshCookies),
            new B10CookieFlowStep(LogoutPath, logoutResponse.StatusCode, logoutCookies),
        ];
    }

    /// <summary>Cookie с требуемым именем (ровно одна) либо отказ с контекстом шага.</summary>
    public static B10SetCookie RequireCookie(
        IReadOnlyList<B10SetCookie> cookies, string name, string step) =>
        cookies.SingleOrDefault(cookie => string.Equals(cookie.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Шаг {step}: cookie «{name}» не выдана (Set-Cookie: " +
                $"{string.Join(" | ", cookies.Select(cookie => cookie.Name))}).");

    /// <summary>SHA-256 (hex, нижний регистр, 64 символа) значения токена (кейс TS-049).</summary>
    public static string Sha256Hex(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
