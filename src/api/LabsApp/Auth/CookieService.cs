using Microsoft.Extensions.Hosting;

namespace LabsApp.Auth;

/// <summary>
/// Реализация IF-004 (FR-008/NFR-007/ADR-009): обе cookie — HttpOnly,
/// SameSite=Strict, Path=/ (Path=/ 7 из 7 Set-Cookie), Max-Age 900/604800;
/// Secure — если и только если окружение ≠ Development (IHostEnvironment).
/// ClearAuth пишет обе cookie с Max-Age=0 и теми же атрибутами — иначе браузер
/// не сопоставит удаляемую cookie с установленной. Автоматическая подмена
/// access по refresh никогда не выполняется — только явный POST /auth/refresh.
/// </summary>
public sealed class CookieService(IHostEnvironment environment) : ICookieService
{
    private readonly IHostEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    /// <inheritdoc/>
    public void SetAccess(HttpContext context, string token)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(token);
        Set(context, AuthCoreDefaults.AccessTokenCookieName, token, AuthCoreDefaults.AccessTokenMaxAgeSeconds);
    }

    /// <inheritdoc/>
    public void SetRefresh(HttpContext context, string token)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(token);
        Set(context, AuthCoreDefaults.RefreshTokenCookieName, token, AuthCoreDefaults.RefreshTokenMaxAgeSeconds);
    }

    /// <inheritdoc/>
    public void ClearAuth(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Удаление: Max-Age=0 с теми же атрибутами (Path, HttpOnly, SameSite, Secure).
        Set(context, AuthCoreDefaults.AccessTokenCookieName, string.Empty, TimeSpan.Zero);
        Set(context, AuthCoreDefaults.RefreshTokenCookieName, string.Empty, TimeSpan.Zero);
    }

    private void Set(HttpContext context, string name, string value, long maxAgeSeconds) =>
        Set(context, name, value, TimeSpan.FromSeconds(maxAgeSeconds));

    private void Set(HttpContext context, string name, string value, TimeSpan maxAge)
    {
        context.Response.Cookies.Append(name, value, new CookieOptions
        {
            Path = AuthCoreDefaults.CookiePath,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            MaxAge = maxAge,
            Secure = !_environment.IsDevelopment(),
        });
    }
}
