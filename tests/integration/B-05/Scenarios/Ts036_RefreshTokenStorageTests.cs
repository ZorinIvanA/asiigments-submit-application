using System.Text.RegularExpressions;
using LabsApp.Auth;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-036 (P0, data_integrity; FR-010) «Refresh-токен: ≥32 байт base64url,
/// в хранилище только SHA-256».
///
/// given: успешный вход (DI-сид студента + POST /auth/login, 200).
/// when:  инспекция cookie refresh_token и IRefreshTokenRepository.
/// then:  cookie содержит открытый токен — base64url-строку ≥43 симв. (≥32 байт);
///        в хранилище сохранена только SHA-256-запись (64 hex) — открытого
///        значения токена в хранилище нет. FR-010 AC «Refresh-токен»;
///        глоссарий «Refresh-токен».
/// </summary>
public sealed class Ts036_RefreshTokenStorageTests(B05SecurityWebAppFactory factory)
    : IClassFixture<B05SecurityWebAppFactory>
{
    private const string Login = "ts036-user";
    private const string Email = "ts036-user@example.com";
    private const string FullName = "Студент ТриДцатьШесть";

    private readonly B05SecurityWebAppFactory _factory = factory;

    [Fact]
    public async Task TS036_LoginRefreshCookie_IsBase64Url32Bytes_StorageKeepsOnlySha256()
    {
        // given: успешный вход.
        var user = B05SecurityClients.SeedStudent(_factory, Login, Email, FullName);
        using var client = B05SecurityClients.CreateClient(_factory);
        using var login = await B05SecurityClients.PostLoginAsync(
            client, Login, B05SecurityClients.CasePassword);
        Assert.True(
            login.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: вход должен вернуть 200, фактически {login.StatusCode}: " +
            $"{await login.Content.ReadAsStringAsync()}");

        // when: инспекция cookie refresh_token.
        var refreshCookie = B05SecurityClients.RequiredSetCookie(login, AuthCoreDefaults.RefreshTokenCookieName);

        // then: открытый токен — base64url ≥43 символов (32 байта CSPRNG).
        Assert.True(
            refreshCookie.Length >= 43,
            $"Cookie refresh_token обязана быть ≥43 символов (32 байта base64url), фактически {refreshCookie.Length}.");
        Assert.True(
            refreshCookie.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'),
            $"Cookie refresh_token обязана быть base64url-строкой, фактически: «{refreshCookie}».");
        var decoded = WebEncoders.Base64UrlDecode(refreshCookie);
        Assert.True(
            decoded.Length >= 32,
            $"Декодированный refresh-токен обязан быть ≥32 байт, фактически {decoded.Length}.");

        // when: инспекция IRefreshTokenRepository.
        var repository = _factory.Services.GetRequiredService<IRefreshTokenRepository>();
        var expectedHash = B05SecurityClients.RefreshTokenHash(refreshCookie);

        // then: хранилище содержит SHA-256-запись (64 hex) и ищется по хэшу.
        Assert.True(
            repository.FindByHash(expectedHash) is not null,
            "В IRefreshTokenRepository нет записи по SHA-256-хэшу значения cookie.");
        Assert.Matches("^[0-9a-f]{64}$", expectedHash);

        // then: открытого значения токена в хранилище нет: поиск по значению даёт пусто,
        //       и ни одна хранимая запись не несёт открытого значения.
        Assert.Null(repository.FindByHash(refreshCookie));
        var storedHashes = B05SecurityClients.StoredRefreshTokenHashes(_factory);
        Assert.Contains(expectedHash, storedHashes);
        foreach (var storedHash in storedHashes)
        {
            Assert.NotEqual(refreshCookie, storedHash);
            Assert.Matches("^[0-9a-f]{64}$", storedHash);
        }

        Assert.True(user.Id != Guid.Empty, "Санити: сид-пользователь существует.");
    }
}
