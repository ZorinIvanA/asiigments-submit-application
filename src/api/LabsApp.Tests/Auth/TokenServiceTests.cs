using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.Tests.Auth;

/// <summary>
/// Юнит-проверки ITokenService (IF-003, FR-008, ADR-008): claims access-JWT
/// (sub/role/iat, exp−iat=900), роль вне {student, teacher} → null, срок по
/// FakeTimeProvider; refresh и reset — непрозрачные base64url ≥43 симв. с
/// хранением только SHA-256 hex (reset — НЕ JWT, TTL 15 минут); код
/// восстановления — ровно 6 ASCII-цифр с возможными ведущими нулями; хэш кода
/// — солёный SHA-256 «sha256$…», НЕ KDF (0 операций счётчика).
/// </summary>
public sealed class TokenServiceTests
{
    private const string JwtKey = "unit-test-jwt-signing-key-0123456789abcdef-0123456789abcdef";
    private const string OtherJwtKey = "other-unit-test-signing-key-fedcba9876543210-fedcba9876543210";

    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("01234567-89ab-4cde-8f01-23456789abcd");

    private static (TokenService Service, FakeTimeProvider Time) CreateService(
        string? jwtKey = null,
        int accessTtlMinutes = AuthOptions.DefaultAccessTtlMinutes,
        int refreshTtlDays = AuthOptions.DefaultRefreshTtlDays)
    {
        var time = new FakeTimeProvider();
        time.SetUtcNow(StartTime);
        var service = new TokenService(
            Options.Create(new AuthOptions
            {
                JwtKey = jwtKey ?? JwtKey,
                AccessTtlMinutes = accessTtlMinutes,
                RefreshTtlDays = refreshTtlDays,
            }),
            time);
        return (service, time);
    }

    // ------------------------------------------------------------------
    // Access-JWT: claims и валидация (AC FR-008: exp−iat=900, sub, role).
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("student")]
    [InlineData("teacher")]
    public void IssueAccessToken_ContainsSubRoleIat_ExpIatEquals900(string role)
    {
        var (service, _) = CreateService();
        var token = service.IssueAccessToken(UserId, role);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(UserId.ToString("D", CultureInfo.InvariantCulture), jwt.Payload.Sub);
        Assert.Equal(role, (string?)jwt.Payload[AuthCoreDefaults.RoleClaimType]);

        var iat = ClaimLong(jwt, "iat");
        var exp = ClaimLong(jwt, "exp");
        Assert.Equal(StartTime.ToUnixTimeSeconds(), iat);
        Assert.Equal(iat + AuthOptions.DefaultAccessTtlMinutes * 60, exp);
    }

    [Fact]
    public void IssueAccessToken_ExpTracksConfiguredTtl()
    {
        var (service, _) = CreateService(accessTtlMinutes: 5);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(service.IssueAccessToken(UserId, "student"));

        Assert.Equal(ClaimLong(jwt, "iat") + 5 * 60, ClaimLong(jwt, "exp"));
    }

    private static long ClaimLong(System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwt, string claimType)
    {
        var value = jwt.Payload[claimType];
        Assert.NotNull(value);
        // Значение claim может приходить примитивом или JsonElement — ToString
        // даёт числовой литерал в обоих случаях.
        return long.Parse(value.ToString()!, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void ValidateAccessToken_Roundtrip_ReturnsUserIdAndRole()
    {
        var (service, _) = CreateService();

        foreach (var role in new[] { "student", "teacher" })
        {
            var payload = service.ValidateAccessToken(service.IssueAccessToken(UserId, role));
            Assert.NotNull(payload);
            Assert.Equal(UserId, payload.UserId);
            Assert.Equal(role, payload.Role);
        }
    }

    // ------------------------------------------------------------------
    // Канал subject (аменда CR-001/ADR-044, IF-003/IF-016): claim «login».
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("student")]
    [InlineData("teacher")]
    public void IssueAccessToken_WithLogin_MintsLoginClaim_PayloadCarriesLogin(string role)
    {
        var (service, _) = CreateService();

        var token = service.IssueAccessToken(UserId, role, "student01");
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);

        // claim «login» выпущен ровно один раз с переданным значением.
        Assert.Equal(
            1,
            jwt.Claims.Count(claim => claim.Type == AuthCoreDefaults.LoginClaimType));
        Assert.Equal("student01", (string?)jwt.Payload[AuthCoreDefaults.LoginClaimType]);

        // Обязательные клеймы сохранены, валидация возвращает Login.
        var payload = service.ValidateAccessToken(token);
        Assert.NotNull(payload);
        Assert.Equal(UserId, payload.UserId);
        Assert.Equal(role, payload.Role);
        Assert.Equal("student01", payload.Login);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IssueAccessToken_WithoutLogin_NoLoginClaim_PayloadLoginNull_TokenValid(string? login)
    {
        var (service, _) = CreateService();

        var token = service.IssueAccessToken(UserId, "student", login);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);

        // claim «login» НЕ минтится (null и пустая строка равнозначны).
        Assert.DoesNotContain(
            jwt.Claims,
            claim => claim.Type == AuthCoreDefaults.LoginClaimType);

        // Толерантность IF-003: токен без claim валиден, Login=null.
        var payload = service.ValidateAccessToken(token);
        Assert.NotNull(payload);
        Assert.Equal(UserId, payload.UserId);
        Assert.Equal("student", payload.Role);
        Assert.Null(payload.Login);
    }

    [Fact]
    public void ValidateAccessToken_TwoArgumentMint_StaysCompatible_LoginNull()
    {
        // Харнес-форма (два аргумента) после аменды: токен валиден, Login=null —
        // поведение идентично состоянию до аменды.
        var (service, _) = CreateService();

        var payload = service.ValidateAccessToken(service.IssueAccessToken(UserId, "teacher"));

        Assert.NotNull(payload);
        Assert.Null(payload.Login);
    }

    [Fact]
    public void ValidateAccessToken_RoleOutsideDictionary_ReturnsNull()
    {
        var (service, _) = CreateService();

        // Чужой role-клейм → null → 401 (IF-003 errors.role_invalid).
        Assert.Null(service.ValidateAccessToken(service.IssueAccessToken(UserId, "admin")));
        Assert.Null(service.ValidateAccessToken(service.IssueAccessToken(UserId, "root")));
    }

    [Fact]
    public void ValidateAccessToken_WrongSigningKey_ReturnsNull()
    {
        var (service, _) = CreateService();
        var (other, _) = CreateService(jwtKey: OtherJwtKey);

        Assert.Null(other.ValidateAccessToken(service.IssueAccessToken(UserId, "student")));
    }

    [Fact]
    public void ValidateAccessToken_TamperedOrNull_ReturnsNull()
    {
        var (service, _) = CreateService();
        var token = service.IssueAccessToken(UserId, "student");
        var tampered = token.Length % 2 == 0 ? token[..^1] + "A" : token[..^1] + "B";

        Assert.Null(service.ValidateAccessToken(tampered));
        Assert.Null(service.ValidateAccessToken(null));
        Assert.Null(service.ValidateAccessToken(string.Empty));
        Assert.Null(service.ValidateAccessToken("garbage"));
    }

    [Fact]
    public void ValidateAccessToken_ExpiredByTimeProvider_ReturnsNull()
    {
        var (service, time) = CreateService(accessTtlMinutes: 15);
        var token = service.IssueAccessToken(UserId, "student");

        time.Advance(TimeSpan.FromMinutes(14));
        Assert.NotNull(service.ValidateAccessToken(token));

        // exp = iat + TTL; ровно на exp токен уже не действителен.
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(service.ValidateAccessToken(token));
    }

    [Fact]
    public void ValidateAccessToken_NotYetValidByTimeProvider_ReturnsNull()
    {
        // nbf: токен, выпущенный «в будущем» относительно другого провайдера
        // времени, отклоняется (срок целиком по TimeProvider).
        var (service, _) = CreateService();
        var token = service.IssueAccessToken(UserId, "student");

        var earlierTime = new FakeTimeProvider(StartTime.AddHours(-1));
        var earlierService = new TokenService(
            Options.Create(new AuthOptions { JwtKey = JwtKey }), earlierTime);

        Assert.Null(earlierService.ValidateAccessToken(token));
    }

    [Fact]
    public void TokenOperations_WithoutConfiguredJwtKey_Throw()
    {
        var time = new FakeTimeProvider();
        var service = new TokenService(Options.Create(new AuthOptions()), time);

        // В хосте Development-ключ подставляет PostConfigure Program; без
        // конфигурации подпись невозможна — явный отказ (guard FR-008 на старте).
        Assert.Throws<InvalidOperationException>(() => service.IssueAccessToken(UserId, "student"));
    }

    // ------------------------------------------------------------------
    // Refresh-токен: base64url ≥43 симв., только SHA-256 hex, TTL по TimeProvider.
    // ------------------------------------------------------------------

    [Fact]
    public void CreateRefreshToken_Base64UrlAtLeast43Chars_HashIsSha256Hex()
    {
        var (service, time) = CreateService(refreshTtlDays: 7);
        var grant = service.CreateRefreshToken(UserId);

        AssertOpaqueToken(grant.Value);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(grant.Value))).ToLowerInvariant(),
            grant.TokenHash);
        Assert.Equal(64, grant.TokenHash.Length);
        Assert.Equal(time.GetUtcNow().AddDays(7).UtcDateTime, grant.ExpiresAt);
    }

    [Fact]
    public void CreateRefreshToken_TwoTokens_AreDistinctRandomValues()
    {
        var (service, _) = CreateService();
        var first = service.CreateRefreshToken(UserId);
        var second = service.CreateRefreshToken(UserId);

        Assert.NotEqual(first.Value, second.Value);
        Assert.NotEqual(first.TokenHash, second.TokenHash);
    }

    // ------------------------------------------------------------------
    // Reset-токен: НЕПРОЗРАЧНАЯ строка (ASM-002), SHA-256 при хранении, TTL 15 мин.
    // ------------------------------------------------------------------

    [Fact]
    public void CreatePasswordResetToken_OpaqueBase64Url_Sha256Hash_Ttl15Minutes()
    {
        var (service, time) = CreateService();
        var grant = service.CreatePasswordResetToken(UserId);

        AssertOpaqueToken(grant.Value);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(grant.Value))).ToLowerInvariant(),
            grant.TokenHash);
        Assert.Equal(time.GetUtcNow().AddMinutes(AuthCoreDefaults.ResetTokenTtlMinutes).UtcDateTime, grant.ExpiresAt);
    }

    [Fact]
    public void CreatePasswordResetToken_ValueIsNotJwt_DoesNotValidateAsAccess()
    {
        var (service, _) = CreateService();
        var grant = service.CreatePasswordResetToken(UserId);

        // Непрозрачный reset не разбирается как JWT и не проходит как access
        // (ASM-002; без purpose-разделения — формат сам по себе несовместим).
        Assert.False(grant.Value.Contains('.', StringComparison.Ordinal));
        Assert.Null(service.ValidateAccessToken(grant.Value));
    }

    [Fact]
    public void CreatePasswordResetToken_TwoTokens_AreDistinct()
    {
        var (service, _) = CreateService();
        var first = service.CreatePasswordResetToken(UserId);
        var second = service.CreatePasswordResetToken(UserId);

        Assert.NotEqual(first.Value, second.Value);
        Assert.NotEqual(first.TokenHash, second.TokenHash);
    }

    // ------------------------------------------------------------------
    // Код восстановления: ровно 6 ASCII-цифр CSPRNG, ведущие нули допустимы.
    // ------------------------------------------------------------------

    [Fact]
    public void GenerateRecoveryCode_AlwaysSixAsciiDigits_LeadingZerosPossible()
    {
        var (service, _) = CreateService();
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        var leadingZeroSeen = false;

        for (var i = 0; i < 2000; i++)
        {
            var code = service.GenerateRecoveryCode();
            Assert.Equal(AuthCoreDefaults.RecoveryCodeDigits, code.Length);
            Assert.All(code, ch => Assert.InRange(ch, '0', '9'));
            leadingZeroSeen |= code.StartsWith('0');
            _ = distinct.Add(code);
        }

        // 2000 выборок из 10^6 CSPRNG: дубликаты почти невозможны, а
        // ведущий нуль (вероятность 0.1 на выборку) гарантированно встречается —
        // формат D6, а не усечённое число.
        Assert.True(distinct.Count >= 1900, $"уникальных кодов {distinct.Count} из 2000");
        Assert.True(leadingZeroSeen, "за 2000 выборок не встретился код с ведущим нулём");
    }

    // ------------------------------------------------------------------
    // Хэш кода восстановления: солёный SHA-256 «sha256$…», НЕ KDF (ASM-005).
    // ------------------------------------------------------------------

    [Fact]
    public void HashRecoveryCode_Format_SaltedSha256_UniqueSalts()
    {
        var (service, _) = CreateService();
        var first = service.HashRecoveryCode("042133");
        var second = service.HashRecoveryCode("042133");

        var parts = first.Split('$');
        Assert.Equal(3, parts.Length);
        Assert.Equal("sha256", parts[0]);
        Assert.Equal(AuthCoreDefaults.RecoveryCodeSaltSizeBytes, Convert.FromBase64String(parts[1]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[2]).Length);

        // Случайная соль: два хэша одного кода различны, оба верифицируются.
        Assert.NotEqual(first, second);
        Assert.True(service.VerifyRecoveryCode("042133", first));
        Assert.True(service.VerifyRecoveryCode("042133", second));
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("042133")]
    [InlineData("999999")]
    public void VerifyRecoveryCode_Roundtrip(string code)
    {
        var (service, _) = CreateService();
        var stored = service.HashRecoveryCode(code);

        Assert.True(service.VerifyRecoveryCode(code, stored));
        Assert.False(service.VerifyRecoveryCode(code == "000000" ? "000001" : "000000", stored));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("sha256$!!!notbase64!!!$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("sha256$AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("md5$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$1000$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void VerifyRecoveryCode_MalformedStored_False(string stored)
    {
        var (service, _) = CreateService();

        Assert.False(service.VerifyRecoveryCode("123456", stored));
    }

    [Fact]
    public void VerifyRecoveryCode_EmptyOrTooLongCode_False()
    {
        var (service, _) = CreateService();
        var stored = service.HashRecoveryCode("123456");

        Assert.False(service.VerifyRecoveryCode(string.Empty, stored));
        Assert.False(service.VerifyRecoveryCode(new string('1', AuthCoreDefaults.PasswordMaxLength + 1), stored));
    }

    [Fact]
    public void RecoveryOperations_PerformZeroKdf()
    {
        // ASM-005/FR-004: recovery/request и recovery/confirm — 0 операций KDF.
        using var counter = new KdfCounter(
            new System.Diagnostics.Metrics.Meter("labs.api", "1.0"), new FakeTimeProvider());
        var service = new TokenService(
            Options.Create(new AuthOptions { Pbkdf2Iterations = TestIterations }), new FakeTimeProvider());

        var code = service.GenerateRecoveryCode();
        var stored = service.HashRecoveryCode(code);
        _ = service.VerifyRecoveryCode(code, stored);

        Assert.Empty(counter.Snapshot());
    }

    private const int TestIterations = 1000;

    private static void AssertOpaqueToken(string value)
    {
        Assert.True(value.Length >= 43, $"длина {value.Length} < 43 (32 байта base64url)");
        Assert.All(value, ch =>
            Assert.True(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_', $"символ {ch} вне base64url"));
    }
}
