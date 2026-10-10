namespace LabsApp.Auth;

/// <summary>Установка и снятие аутентификационных cookie (IF-004, FR-008/NFR-007).</summary>
public interface ICookieService
{
    /// <summary>
    /// Ставит Set-Cookie access_token: HttpOnly, SameSite=Strict, Path=/,
    /// Max-Age=900; Secure — если и только если окружение ≠ Development
    /// (признак IHostEnvironment).
    /// </summary>
    void SetAccess(HttpContext context, string token);

    /// <summary>
    /// Ставит Set-Cookie refresh_token: те же атрибуты, Max-Age=604800 (Path=/).
    /// </summary>
    void SetRefresh(HttpContext context, string token);

    /// <summary>Снимает обе cookie: Max-Age=0 с теми же атрибутами.</summary>
    void ClearAuth(HttpContext context);
}
