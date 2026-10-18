namespace LabsApp.Auth;

/// <summary>
/// Константы Api.Auth.Core (C-004): имя схемы аутентификации, имена/атрибуты
/// cookie (IF-004), claims токенов (IF-003), TTL reset-токена и границы полей
/// для ключей лимитеров (IF-006). Единый источник имён cookie для ICookieService
/// и потребителей (тестовые харнесы минтят access-cookie по этим же константам).
/// </summary>
public static class AuthCoreDefaults
{
    /// <summary>Имя схемы аутентификации и схемы по умолчанию (ADR-003).</summary>
    public const string AuthenticationScheme = "Auth.Core";

    /// <summary>Имя cookie с access-JWT (IF-004).</summary>
    public const string AccessTokenCookieName = "access_token";

    /// <summary>Имя cookie с refresh-токеном (IF-004).</summary>
    public const string RefreshTokenCookieName = "refresh_token";

    /// <summary>Path обеих auth-cookie (IF-004/NFR-007: Path=/ 7 из 7 Set-Cookie).</summary>
    public const string CookiePath = "/";

    /// <summary>Max-Age cookie access_token, секунд (IF-004/NFR-007: 900).</summary>
    public const long AccessTokenMaxAgeSeconds = 900;

    /// <summary>Max-Age cookie refresh_token, секунд (IF-004/NFR-007: 604800 = 7 суток).</summary>
    public const long RefreshTokenMaxAgeSeconds = 604_800;

    /// <summary>Детерминированный текст 401 (FR-022).</summary>
    public const string UnauthorizedMessage = "Не авторизован";

    /// <summary>TTL токена сброса пароля, минут (IF-003: 15).</summary>
    public const int ResetTokenTtlMinutes = 15;

    /// <summary>Длина refresh/reset-токена, случайных байт CSPRNG (IF-003: ≥256 бит).</summary>
    public const int RefreshTokenSizeBytes = 32;

    /// <summary>Длина соли хэша кода восстановления, байт (IF-003).</summary>
    public const int RecoveryCodeSaltSizeBytes = 16;

    /// <summary>Длина кода восстановления в ASCII-цифрах (IF-003: 6, ведущие нули допустимы).</summary>
    public const int RecoveryCodeDigits = 6;

    /// <summary>
    /// Верхняя граница длины пароля/кода при проверке (ASM-015): кандидат
    /// длиннее — Verify/VerifyRecoveryCode возвращают false немедленно, без KDF.
    /// </summary>
    public const int PasswordMaxLength = 128;

    /// <summary>Граница поля логина для ключей лимитеров (IF-006: login_ci≤100).</summary>
    public const int LoginKeyMaxLength = 100;

    /// <summary>Граница поля email для ключей лимитеров (IF-006: email_ci≤254).</summary>
    public const int EmailKeyMaxLength = 254;

    /// <summary>Claim роли в access-JWT (IF-003: sub/role/iat/exp).</summary>
    public const string RoleClaimType = "role";

    /// <summary>
    /// Claim логина субъекта (аменда CR-001/ADR-044, IF-003/IF-016): минтится в
    /// access-JWT ТОЛЬКО при непустом login (толерантность к токенам без claim —
    /// Login=null), прокидывается CookieAuthenticationHandler'ом в principal и
    /// читается потребителями наблюдаемости как subject записей «Api.Security».
    /// </summary>
    public const string LoginClaimType = "login";
}
