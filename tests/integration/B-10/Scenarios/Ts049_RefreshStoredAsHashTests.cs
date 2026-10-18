using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-049 «Refresh хранится только SHA-256-хэшем» (data_integrity, FR-008/NFR-006, P0).
///
/// given: выполнен успешный вход (учётные данные сида); значение refresh-cookie
///        зафиксировано.
/// when:  инспекция тестового шова хранилища-заглушки ISecurityTokenRepository
///        (до приземления консолидации T-101 шов разрешает IRefreshTokenRepository
///        с поиском записи FindByHash — после консолидации FindLiveByHash;
///        см. B10SecurityTokenStoreSeam).
/// then:  запись RefreshToken содержит SHA-256 (hex, 64 символа) значения токена,
///        expiresAt=now+7 дней, revokedAt=null; само значение токена в хранилище
///        отсутствует (FR-008 AC «Refresh хранится хэшем»).
/// </summary>
public sealed class Ts049_RefreshStoredAsHashTests : IClassFixture<B10NoDemoWebAppFactory>
{
    /// <summary>Допуск на «expiresAt=now+7 дней»: TTL назначается при выпуске токена.</summary>
    private static readonly TimeSpan ExpiresAtTolerance = TimeSpan.FromMinutes(10);

    private readonly B10NoDemoWebAppFactory _factory;

    public Ts049_RefreshStoredAsHashTests(B10NoDemoWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RefreshTokenIsStoredOnlyAsSha256Hash_WithSevenDayTtlAndNoRevocation()
    {
        // given: выполнен успешный вход; значение refresh-cookie зафиксировано.
        var beforeLogin = DateTime.UtcNow;
        using var client = HostClients.Create(_factory);
        using var response = await client.PostAsJsonAsync(
            B10CookieFlow.LoginPath,
            new
            {
                login = SeedOptions.DefaultTeacherLogin,
                password = SeedOptions.DefaultTeacherPassword,
            });
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: успешный вход сеяным учителем — ожидался 200, фактически " +
            $"{(int)response.StatusCode} {response.StatusCode}.");
        var refreshToken = B10CookieFlow
            .RequireCookie(B10SetCookieReader.Read(response), AuthCoreDefaults.RefreshTokenCookieName, "login")
            .Value;
        Assert.False(
            string.IsNullOrEmpty(refreshToken),
            "refresh-cookie выдана с пустым значением — шаг given кейса неисполним.");
        var expectedHash = B10CookieFlow.Sha256Hex(refreshToken);
        var afterLogin = DateTime.UtcNow;

        // when: инспекция тестового шова хранилища-заглушки по хэшу значения.
        var seam = new B10SecurityTokenStoreSeam(_factory.Services);
        var record = seam.FindByHash(expectedHash);
        Assert.True(
            record is not null,
            $"В хранилище токенов нет записи с TokenHash = SHA-256(refresh) ({expectedHash}) — " +
            "refresh-токен не хранится SHA-256-хэшем значения cookie.");

        // then: TokenHash записи — SHA-256 значения (hex, ровно 64 символа).
        var storedHash = B10SecurityTokenStoreSeam.StringProperty(record!, "TokenHash");
        Assert.True(
            storedHash.Length == 64 && storedHash.All(char.IsAsciiHexDigit),
            $"TokenHash должен быть hex-строкой из 64 символов, фактически «{storedHash}».");
        Assert.Equal(expectedHash, storedHash);

        // then: expiresAt = now + 7 дней (Auth__RefreshTtlDays умолчание 7).
        var expiresAt = B10SecurityTokenStoreSeam.DateTimeProperty(record!, "ExpiresAt");
        Assert.True(
            expiresAt.Kind == DateTimeKind.Utc,
            $"ExpiresAt должен быть UTC-меткой (ADR-005), фактически Kind={expiresAt.Kind}.");
        Assert.True(
            expiresAt >= beforeLogin.AddDays(7) - ExpiresAtTolerance
            && expiresAt <= afterLogin.AddDays(7) + ExpiresAtTolerance,
            $"ExpiresAt должен быть now+7 дней (окно входа {beforeLogin:O}..{afterLogin:O}), " +
            $"фактически {expiresAt:O}.");
        Assert.True(
            expiresAt > beforeLogin,
            "ExpiresAt должен лежать в будущем (TTL refresh — 7 дней).");

        // then: revokedAt=null (токен действителен сразу после выпуска).
        Assert.Null(B10SecurityTokenStoreSeam.Property(record!, "RevokedAt"));

        // then: само значение токена в хранилище отсутствует — сырая строка не
        // является ключом записи, а хранящийся хэш не равен значению токена.
        Assert.True(
            !string.Equals(storedHash, refreshToken, StringComparison.Ordinal),
            "В хранилище обнаружено само значение refresh-токена в поле TokenHash.");
        Assert.True(
            seam.FindByHash(refreshToken) is null,
            "Значение refresh-токена найдено в хранилище как ключ записи — исходное значение " +
            "токена хранится вопреки FR-008 AC «Refresh хранится хэшем».");
    }
}
