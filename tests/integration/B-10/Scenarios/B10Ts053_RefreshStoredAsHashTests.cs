using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-053 «Refresh хранится только SHA-256-хэшем» (data_integrity, FR-008, P1).
///
/// given: успешный вход; значение refresh-токена из Set-Cookie зафиксировано
///        тестом.
/// when:  инспекция хранилища-заглушки (ISecurityTokenRepository/in-memory
///        записи RefreshToken — тестовый шов B10SecurityTokenStoreSeam).
/// then:  запись содержит tokenHash = 64-символьный hex (SHA-256 значения), но
///        не само значение; само значение нигде в хранилище не встречается
///        (FR-008 AC «Refresh хранится хэшем»).
/// </summary>
public sealed class B10Ts053_RefreshStoredAsHashTests : IClassFixture<B10NoDemoWebAppFactory>
{
    /// <summary>Допуск на «expiresAt = now + 7 дней»: TTL назначается при выпуске.</summary>
    private static readonly TimeSpan ExpiresAtTolerance = TimeSpan.FromMinutes(10);

    private readonly B10NoDemoWebAppFactory _factory;

    public B10Ts053_RefreshStoredAsHashTests(B10NoDemoWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RefreshTokenIsStoredOnlyAsSha256Hash_RawValueNowhereInStore()
    {
        // given: успешный вход; значение refresh-токена из Set-Cookie зафиксировано.
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

        // when: инспекция хранилища-заглушки — поиск записи по SHA-256 значения.
        var seam = new B10SecurityTokenStoreSeam(_factory.Services);
        var record = seam.FindByHash(expectedHash);
        Assert.True(
            record is not null,
            $"В хранилище токенов нет записи с tokenHash = SHA-256(refresh) ({expectedHash}) — " +
            "refresh-токен не хранится SHA-256-хэшем значения cookie.");

        // then: tokenHash записи — 64-символьный hex, равный SHA-256 значения.
        var storedHash = B10SecurityTokenStoreSeam.StringProperty(record!, "TokenHash");
        Assert.True(
            storedHash.Length == 64 && storedHash.All(char.IsAsciiHexDigit),
            $"tokenHash должен быть hex-строкой из 64 символов, фактически «{storedHash}».");
        Assert.Equal(expectedHash, storedHash);

        // then: expiresAt = now + 7 дней (Auth__RefreshTtlDays умолчание 7).
        var expiresAt = B10SecurityTokenStoreSeam.DateTimeProperty(record!, "ExpiresAt");
        Assert.True(
            expiresAt.Kind == DateTimeKind.Utc,
            $"ExpiresAt должен быть UTC-меткой (ADR-004), фактически Kind={expiresAt.Kind}.");
        Assert.True(
            expiresAt >= beforeLogin.AddDays(7) - ExpiresAtTolerance
            && expiresAt <= afterLogin.AddDays(7) + ExpiresAtTolerance,
            $"ExpiresAt должен быть now+7 дней (окно входа {beforeLogin:O}..{afterLogin:O}), " +
            $"фактически {expiresAt:O}.");

        // then: само значение нигде в хранилище не встречается — хранящийся хэш
        // не равен значению токена, и по значению токена как ключу запись не
        // находится (записи хранятся ТОЛЬКО по SHA-256-хэшу значения).
        Assert.True(
            !string.Equals(storedHash, refreshToken, StringComparison.Ordinal),
            "В хранилище обнаружено само значение refresh-токена в поле tokenHash.");
        Assert.True(
            seam.FindByHash(refreshToken) is null,
            "Значение refresh-токена найдено в хранилище как ключ записи — исходное значение " +
            "токена хранится вопреки FR-008 AC «Refresh хранится хэшем».");
    }
}
