using LabsApp.Auth;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-060 (P1, negative; FR-009) «Refresh: неизвестное значение токена — 401».
/// given: В хранилище нет записи с SHA-256 предъявляемого значения (случайная
///        строка ≥44 симв. — детерминированная base64-строка 32 байтов, 44
///        символа: значение заведомо не выпускалось хостом).
/// when:  POST /auth/refresh с cookie refresh_token=&lt;мусорное значение&gt;.
/// then:  401 'Не авторизован'; без Set-Cookie (FR-009 AC «токен неизвестен —
///        нет записи по SHA-256»).
/// </summary>
public sealed class Ts060_RefreshUnknownTokenValueTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    /// <summary>
    /// Детерминированная «мусорная» строка (base64 32 байтов, 44 символа) —
    /// длина значений refresh-токенов (≥256 бит), но в хранилище нет записи с её
    /// SHA-256 (хост такие значения не выпускает).
    /// </summary>
    private static readonly string UnknownTokenValue = Convert.ToBase64String(new byte[32]);

    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS060_Refresh_WithUnknownTokenValue_Returns401WithoutSetCookie()
    {
        // given: в хранилище нет записи с SHA-256 предъявляемого значения
        // (свежий хост: вход не выполнялся, значение не выпускалось).
        Assert.True(UnknownTokenValue.Length >= 44, $"Мусорное значение короче 44 символов: {UnknownTokenValue.Length}.");
        using var client = HostClients.Create(_factory);
        HostClients.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, UnknownTokenValue);

        // when: POST /auth/refresh с cookie refresh_token=<мусорное значение>.
        using var response = await HostClients.PostWithoutBodyAsync(client, HostClients.RefreshPath);

        // then: 401 'Не авторизован'; без Set-Cookie.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Unauthorized,
            "Не авторизован",
            exactSingleMessageProperty: true);
        Assert.False(response.Headers.Contains("Set-Cookie"), "401 на неизвестный refresh не должен выставлять cookie.");
    }
}
