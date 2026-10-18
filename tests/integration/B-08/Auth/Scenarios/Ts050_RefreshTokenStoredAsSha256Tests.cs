using System.Text.RegularExpressions;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Auth.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Auth.Scenarios;

/// <summary>
/// TS-050 «Refresh-токен хранится только SHA-256-хэшем» (data_integrity,
/// FR-008, P1).
///
/// given: успешный вход; тесту доступно хранилище-заглушка
///        ISecurityTokenRepository и значение refresh-cookie.
/// when:  инспекция записей RefreshToken в хранилище.
/// then:  запись содержит SHA-256 (hex, 64 символа) значения токена, но не
///        само значение (FR-008 AC «Refresh хранится хэшем»).
///
/// «Хранилище-заглушка» — in-memory ISecurityTokenRepository тестового хоста
/// (FR-024: реализация хранилища в приложении in-memory; заглушать нечего —
/// интерфейс и есть контракт инспекции).
/// </summary>
public sealed class Ts050_RefreshTokenStoredAsSha256Tests
{
    private const string Login = "refreshhash";
    private const string Email = "refreshhash@example.com";

    [Fact]
    public async Task SuccessfulLogin_RefreshRecord_StoresOnlySha256HexOfValue()
    {
        using var factory = new B08AuthDevFactory();
        using var client = B08AuthHost.CreateClient(factory);

        // given: пользователь существует и вошёл успешно; значение refresh-cookie известно тесту.
        B08AuthHost.SeedUser(factory, Login, Email, "Рефреш Хэш", UserRoles.Student);
        using var login = await client.PostAsync(
            B08AuthHost.LoginEndpoint,
            "{\"login\":\"" + Login + "\",\"password\":\"" + B08AuthHost.TestUserPassword + "\"}");
        B08AuthHost.AssertStatus(login, HttpStatusCode.OK, "успешный вход (TS-050)");
        var refreshCookie = B08AuthHost.SingleCookie(login, "refresh_token", "Set-Cookie входа (TS-050)");

        // given: хранилище токенов доступно тесту (DI тестового хоста).
        var securityTokens = factory.Services.GetRequiredService<LabsApp.Storage.ISecurityTokenRepository>();

        // when: инспекция записей RefreshToken — поиск по SHA-256 значения cookie
        // (зеркало контракта IF-003: в хранилище — только SHA-256 hex значения).
        var expectedHash = B08AuthHost.Sha256Hex(refreshCookie.Value);
        var record = securityTokens.FindLiveByHash(expectedHash);

        // then: запись содержит SHA-256 (hex, 64 символа) значения токена.
        Assert.NotNull(record);
        Assert.Equal(expectedHash, record!.TokenHash);
        Assert.True(Regex.IsMatch(record.TokenHash, "^[0-9a-f]{64}$"), $"TokenHash не hex-64: «{record.TokenHash}».");

        // then: …но не само значение: в записи значением является хэш, а не
        // токен; поиск по «сырому» значению не находит записи.
        Assert.NotEqual(refreshCookie.Value, record.TokenHash);
        Assert.Null(securityTokens.FindLiveByHash(refreshCookie.Value));
    }
}
