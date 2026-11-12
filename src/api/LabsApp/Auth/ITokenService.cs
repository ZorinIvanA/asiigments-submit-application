namespace LabsApp.Auth;

/// <summary>
/// Разобранный access-JWT (IF-003): {userId, role, login?}. Login — claim
/// «login» (аменда CR-001/ADR-044: канал subject, IF-016); токен БЕЗ claim
/// валиден (толерантность к выпускам до аменды и тестовым харнессам с
/// двухаргументной формой) — Login=null.
/// </summary>
public sealed record AccessTokenPayload(Guid UserId, string Role, string? Login = null);

/// <summary>
/// Свежевыпущенный refresh-токен (IF-003): открытое значение
/// (base64url(32 байта CSPRNG) ≥43 симв.) возвращается ТОЛЬКО вызывающему для
/// установки cookie; в хранилище кладётся только <see cref="TokenHash"/>
/// (SHA-256 hex значения).
/// </summary>
public sealed record RefreshTokenGrant(string Value, string TokenHash, DateTime ExpiresAt);

/// <summary>
/// Свежевыпущенный токен сброса пароля (IF-003, ASM-002): непрозрачная строка
/// base64url(32 байта CSPRNG) — НЕ JWT; в хранилище кладётся только
/// <see cref="TokenHash"/> (SHA-256 hex значения); TTL — 15 минут.
/// </summary>
public sealed record PasswordResetTokenGrant(string Value, string TokenHash, DateTime ExpiresAt);

/// <summary>
/// Выпуск и валидация токенов и кодов (IF-003, FR-008): access — JWT HS256
/// ключом Auth__JwtKey (claims sub/role/iat, exp = iat + Auth__AccessTtlMinutes);
/// refresh и reset — непрозрачные base64url-строки ≥256 бит CSPRNG с хранением
/// только SHA-256-хэша; код восстановления — ровно 6 ASCII-цифр CSPRNG
/// (ведущие нули допустимы), хэш кода — быстрый солёный SHA-256
/// «sha256$&lt;saltBase64&gt;$&lt;hashBase64&gt;» — НЕ KDF (ASM-005: 0 операций
/// KDF на recovery-ветках). Единственный источник случайности секретов —
/// RandomNumberGenerator; TTL вычисляются по TimeProvider; значения токенов,
/// кодов и их хэши не логируются (NFR-006).
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Выпускает access-JWT (HS256) с claims sub/role/iat и exp = iat + AccessTtlMinutes;
    /// при НЕпустом <paramref name="login"/> — дополнительный claim «login» (канал
    /// subject, IF-016). Третий параметр опционален для совместимости тестовых
    /// харнессов; в прод-коде вызывается ТОЛЬКО трёхаргументная форма (ADR-044).
    /// </summary>
    string IssueAccessToken(Guid userId, string role, string? login = null);

    /// <summary>
    /// Валидирует access-JWT: подпись, срок по TimeProvider, sub, роль из
    /// {student, teacher} — роль вне словаря → null (вызывающий отвечает 401).
    /// </summary>
    AccessTokenPayload? ValidateAccessToken(string? token);

    /// <summary>Генерирует refresh-токен (base64url(32B CSPRNG)) и его SHA-256 hex-хэш (TTL Auth__RefreshTtlDays).</summary>
    RefreshTokenGrant CreateRefreshToken(Guid userId);

    /// <summary>Генерирует непрозрачный токен сброса пароля (base64url(32B CSPRNG), SHA-256-хэш, TTL 15 минут).</summary>
    PasswordResetTokenGrant CreatePasswordResetToken(Guid userId);

    /// <summary>Генерирует код восстановления: ровно 6 ASCII-цифр из CSPRNG (ведущие нули допустимы).</summary>
    string GenerateRecoveryCode();

    /// <summary>Хэширует код восстановления солёным SHA-256 «sha256$&lt;saltBase64&gt;$&lt;hashBase64&gt;» (соль 16 байт, НЕ KDF).</summary>
    string HashRecoveryCode(string code);

    /// <summary>
    /// Проверяет код против хранимой строки «sha256$…»: malformed-хэш → false;
    /// сравнение — постоянное время; KDF не выполняется.
    /// </summary>
    bool VerifyRecoveryCode(string code, string stored);
}
