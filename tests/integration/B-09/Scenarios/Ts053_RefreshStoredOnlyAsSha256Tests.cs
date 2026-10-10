using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B09.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-053 «Токены: refresh хранится только SHA-256-хэшем» (data_integrity,
/// FR-008 + NFR-006, P1).
///
/// given: успешный вход; значение refresh-cookie сохранено тестом.
/// when:  инспекция хранилища-заглушки (ISecurityTokenRepository).
/// then:  запись RefreshToken содержит SHA-256 (hex, 64 символа) значения
///        токена и НЕ содержит само значение; expiresAt = now+7 дней
///        (AC FR-008 «Refresh хранится хэшем»; Auth__RefreshTtlDays=7).
///
/// «Хранилище-заглушка» — in-memory ISecurityTokenRepository тестового хоста
/// (FR-024); now — фиктивное время фикстуры (FakeTimeProvider): часы стоят,
/// поэтому expiresAt сравнивается с точным моментом входа + 7 суток.
/// Файл текущей волны батча B-09 (перенумерация кейсов): файл прежней волны
/// (Ts050_RefreshTokenStoredAsSha256) не изменялся.
/// </summary>
public sealed class Ts053_RefreshStoredOnlyAsSha256Tests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.54";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task RefreshCookieValue_StoredOnlyAsSha256Hex_WithExpiresAtNowPlusSevenDays()
    {
        // given: успешный вход; значение refresh-cookie сохранено тестом.
        _ = _factory.Services;
        using var client = B09AuthHttp.Create(_factory, TestIp);
        using var login = await B09AuthHttp.LoginAsync(client, "teacher", B09AuthHttp.TeacherPassword);
        _ = await B09Assertions.ParseObjectAsync(login, HttpStatusCode.OK, "успешный вход (TS-053)");
        var refreshCookie = B09AuthSupport.SingleCookie(login, "refresh_token", "Set-Cookie входа (TS-053)");

        // given: хранилище-заглушка доступно тесту (DI тестового хоста).
        var securityTokens = _factory.Services.GetRequiredService<ISecurityTokenRepository>();

        // when: инспекция хранилища — поиск записи refresh-токена по SHA-256
        // значения cookie (зеркало IF-003: в хранилище только хэш значения).
        var expectedHash = B09AuthSupport.Sha256Hex(refreshCookie.Value);
        var record = securityTokens.FindLiveByHash(expectedHash);

        // then: запись содержит SHA-256 (hex, 64 символа) значения токена.
        Assert.NotNull(record);
        Assert.Equal(expectedHash, record!.TokenHash);
        Assert.True(Regex.IsMatch(record.TokenHash, "^[0-9a-f]{64}$"), $"TokenHash не hex-64: «{record.TokenHash}».");

        // then: …и НЕ содержит само значение.
        Assert.NotEqual(refreshCookie.Value, record.TokenHash);
        Assert.Null(securityTokens.FindLiveByHash(refreshCookie.Value));

        // then: expiresAt = now + 7 дней (Auth__RefreshTtlDays=7; часы фикстуры
        // стоят — момент создания и момент инспекции совпадают).
        var now = _factory.Time.GetUtcNow();
        Assert.Equal(now.AddDays(7).UtcDateTime, record.ExpiresAt);
    }
}
