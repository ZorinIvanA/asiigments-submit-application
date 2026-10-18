using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B09.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-050 «Refresh-токен хранится только SHA-256-хэшем» (data_integrity,
/// FR-008, P0).
///
/// given: успешный вход; тесту доступно хранилище-заглушка
///        ISecurityTokenRepository и значение refresh-cookie.
/// when:  инспекция записей RefreshToken в хранилище.
/// then:  запись содержит SHA-256 (hex, 64 символа) значения токена, но не
///        само значение (FR-008 AC «Refresh хранится хэшем»).
///
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-08/Auth. Поведенческая часть кейса исполнима дословно и
/// исполнена в собственной зоне батча B-09 (прецедент c-1052); расхождение
/// размещения зафиксировано в scenario_change_requests.
///
/// «Хранилище-заглушка» — in-memory ISecurityTokenRepository тестового хоста
/// (FR-024: реализация хранилища в приложении in-memory; интерфейс и есть
/// контракт инспекции).
/// </summary>
public sealed class Ts050_RefreshTokenStoredAsSha256Tests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.50";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task SuccessfulLogin_RefreshRecord_StoresOnlySha256HexOfValue()
    {
        // given: успешный вход; значение refresh-cookie известно тесту.
        _ = _factory.Services;
        using var client = B09AuthHttp.Create(_factory, TestIp);
        using var login = await B09AuthHttp.LoginAsync(client, "teacher", B09AuthHttp.TeacherPassword);
        _ = await B09Assertions.ParseObjectAsync(login, HttpStatusCode.OK, "успешный вход (TS-050)");
        var refreshCookie = B09AuthSupport.SingleCookie(login, "refresh_token", "Set-Cookie входа (TS-050)");

        // given: хранилище токенов доступно тесту (DI тестового хоста).
        var securityTokens = _factory.Services.GetRequiredService<ISecurityTokenRepository>();

        // when: инспекция записей RefreshToken — поиск по SHA-256 значения cookie
        // (зеркало контракта IF-003: в хранилище — только SHA-256 hex значения).
        var expectedHash = B09AuthSupport.Sha256Hex(refreshCookie.Value);
        var record = securityTokens.FindLiveByHash(expectedHash);

        // then: запись содержит SHA-256 (hex, 64 символа) значения токена.
        Assert.NotNull(record);
        Assert.Equal(expectedHash, record!.TokenHash);
        Assert.True(Regex.IsMatch(record.TokenHash, "^[0-9a-f]{64}$"), $"TokenHash не hex-64: «{record.TokenHash}».");

        // then: …но не само значение: значением записи является хэш, поиск по
        // «сырому» значению записи не находит.
        Assert.NotEqual(refreshCookie.Value, record.TokenHash);
        Assert.Null(securityTokens.FindLiveByHash(refreshCookie.Value));
    }
}
