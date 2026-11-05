using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LabsApp.Auth;

/// <summary>
/// Реализация IF-003 (FR-008/ADR-008): access — JWT HS256 ключом Auth__JwtKey
/// (claims sub/role/iat, exp = iat + Auth__AccessTtlMinutes; при непустом login —
/// дополнительный claim «login», аменда CR-001/ADR-044; срок проверяется
/// ПО TimeProvider — детерминированные TTL-тесты; подпись валидирует
/// <see cref="JwtSecurityTokenHandler"/>, MapInboundClaims = false); роль вне
/// {student, teacher} → null (IF-003). Refresh и reset — непрозрачные
/// base64url(32 байта CSPRNG) с SHA-256-hex хэшем значения: reset больше НЕ
/// JWT (ASM-002), одноразовость/TTL обеспечивает хранилище токенов (IF-015,
/// зона C-003/C-013). Код восстановления — 6 ASCII-цифр CSPRNG (формат D6 —
/// ведущие нули допустимы); хэш кода — солёный SHA-256 «sha256$salt$hash»,
/// НЕ KDF (ASM-005). Случайность — только RandomNumberGenerator.
/// </summary>
public sealed class TokenService : ITokenService
{
    // Обработчик потокобезопасен для ValidateToken/WriteToken (без мутации состояния);
    // MapInboundClaims = false — claims читаются по исходным именам (sub/role).
    private static readonly JwtSecurityTokenHandler Handler = new() { MapInboundClaims = false };

    private readonly IOptions<AuthOptions> _options;
    private readonly TimeProvider _timeProvider;

    public TokenService(IOptions<AuthOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public string IssueAccessToken(Guid userId, string role, string? login = null)
    {
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.Value.AccessTtlMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString("D", CultureInfo.InvariantCulture)),
            new(AuthCoreDefaults.RoleClaimType, role),
            new(
                JwtRegisteredClaimNames.Iat,
                now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
        };
        if (!string.IsNullOrEmpty(login))
        {
            // Канал subject (аменда CR-001/ADR-044, IF-016): claim «login» минтится
            // ТОЛЬКО при непустом значении; его отсутствие токен не инвалидирует.
            claims.Add(new Claim(AuthCoreDefaults.LoginClaimType, login));
        }

        var jwt = new JwtSecurityToken(
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: CreateSigningCredentials());
        return Handler.WriteToken(jwt);
    }

    /// <inheritdoc/>
    public AccessTokenPayload? ValidateAccessToken(string? token)
    {
        var principal = TryValidate(token);
        if (principal is null)
        {
            return null;
        }

        var role = principal.FindFirstValue(AuthCoreDefaults.RoleClaimType);
        if (role is not (UserRoles.Student or UserRoles.Teacher))
        {
            // Роль вне словаря → null → 401 (IF-003).
            return null;
        }

        if (!TryGetGuidClaim(principal, JwtRegisteredClaimNames.Sub, out var userId)
            || !IsAlive(principal, _timeProvider.GetUtcNow()))
        {
            return null;
        }

        // Толерантность IF-003 (аменда CR-001/ADR-044): claim «login» отсутствует —
        // токен валиден, Login=null (выпуски до аменды и тестовые харнессы).
        var login = principal.FindFirstValue(AuthCoreDefaults.LoginClaimType);
        return new AccessTokenPayload(userId, role, login);
    }

    /// <inheritdoc/>
    public RefreshTokenGrant CreateRefreshToken(Guid userId)
    {
        // userId фиксирует владельца в репозитории у вызывающего; сам токен от него
        // не зависит (значение — чистая CSPRNG-случайность).
        _ = userId;

        var value = NewOpaqueToken();
        return new RefreshTokenGrant(
            value,
            Sha256Hex(value),
            _timeProvider.GetUtcNow().AddDays(_options.Value.RefreshTtlDays).UtcDateTime);
    }

    /// <inheritdoc/>
    public PasswordResetTokenGrant CreatePasswordResetToken(Guid userId)
    {
        _ = userId;

        var value = NewOpaqueToken();
        return new PasswordResetTokenGrant(
            value,
            Sha256Hex(value),
            _timeProvider.GetUtcNow().AddMinutes(AuthCoreDefaults.ResetTokenTtlMinutes).UtcDateTime);
    }

    /// <inheritdoc/>
    public string GenerateRecoveryCode() =>
        RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, AuthCoreDefaults.RecoveryCodeDigits))
            .ToString(new string('0', AuthCoreDefaults.RecoveryCodeDigits), CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string HashRecoveryCode(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        var salt = RandomNumberGenerator.GetBytes(AuthCoreDefaults.RecoveryCodeSaltSizeBytes);
        var hash = Sha256(Combine(salt, code));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{RecoveryHashMarker}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    /// <inheritdoc/>
    public bool VerifyRecoveryCode(string code, string stored)
    {
        if (string.IsNullOrEmpty(code)
            || string.IsNullOrEmpty(stored)
            || code.Length > AuthCoreDefaults.PasswordMaxLength)
        {
            return false;
        }

        var parts = stored.Split('$');
        if (parts.Length != 3
            || !string.Equals(parts[0], RecoveryHashMarker, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Sha256(Combine(salt, code));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private const string RecoveryHashMarker = "sha256";

    private static byte[] Combine(byte[] salt, string value)
    {
        // Порядок «соль || значение»: соль случайна и уникальна для каждого хэша.
        var tail = Encoding.UTF8.GetBytes(value);
        var buffer = new byte[salt.Length + tail.Length];
        Buffer.BlockCopy(salt, 0, buffer, 0, salt.Length);
        Buffer.BlockCopy(tail, 0, buffer, salt.Length, tail.Length);
        return buffer;
    }

    private static byte[] Sha256(byte[] data) => SHA256.HashData(data);

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>Непрозрачный токен: base64url(32 байта CSPRNG) — ≥43 символа (≥256 бит).</summary>
    private static string NewOpaqueToken() =>
        WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(AuthCoreDefaults.RefreshTokenSizeBytes));

    private ClaimsPrincipal? TryValidate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            // Срок (ValidateLifetime) не проверяется библиотекой: exp/nbf сверяются
            // с TimeProvider ниже (глоссарий — детерминированные TTL-тесты).
            return Handler.ValidateToken(token, CreateValidationParameters(), out _);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            // Неверная подпись/формат/структура (в т.ч. непрозрачные refresh/reset)
            // — невалидный access-токен.
            return null;
        }
    }

    private TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = false,
        ValidateIssuerSigningKey = true,
        RequireSignedTokens = true,
        IssuerSigningKey = CreateSigningKey(),
        ClockSkew = TimeSpan.Zero,
    };

    private SigningCredentials CreateSigningCredentials() => new(CreateSigningKey(), SecurityAlgorithms.HmacSha256);

    private SymmetricSecurityKey CreateSigningKey()
    {
        var jwtKey = _options.Value.JwtKey;
        if (string.IsNullOrEmpty(jwtKey))
        {
            // Development-умолчание подставляет PostConfigure в Program; отсутствие
            // ключа — дефект конфигурации (Production-guard FR-008 валидирует на старте).
            throw new InvalidOperationException("Auth__JwtKey не сконфигурирован: подпись JWT невозможна.");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    }

    /// <summary>
    /// Срок по TimeProvider: жив, пока now &lt; exp (строго) и now ≥ nbf; отсутствие
    /// exp — невалидный токен (fail-closed).
    /// </summary>
    private static bool IsAlive(ClaimsPrincipal principal, DateTimeOffset now)
    {
        if (!TryGetLongClaim(principal, JwtRegisteredClaimNames.Exp, out var expiresAtSeconds)
            || now.ToUnixTimeSeconds() >= expiresAtSeconds)
        {
            return false;
        }

        if (TryGetLongClaim(principal, JwtRegisteredClaimNames.Nbf, out var notBeforeSeconds)
            && now.ToUnixTimeSeconds() < notBeforeSeconds)
        {
            return false;
        }

        return true;
    }

    private static bool TryGetLongClaim(ClaimsPrincipal principal, string claimType, out long value)
    {
        value = 0;
        var raw = principal.FindFirstValue(claimType);
        return raw is not null
            && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetGuidClaim(ClaimsPrincipal principal, string claimType, out Guid value)
    {
        value = Guid.Empty;
        var raw = principal.FindFirstValue(claimType);
        return raw is not null && Guid.TryParse(raw, out value);
    }
}
