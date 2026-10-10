using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B07.Infrastructure;

/// <summary>
/// Помощники auth-сценариев батча B-07 (TS-046..TS-060): эндпоинты /api/v1/auth
/// (register — FR-006, login — FR-007), клиенты с фиксированным транспортным
/// IP (given кейсов, см.
/// <see cref="B07TestClientIpResolver"/>), разбор Set-Cookie (значения
/// access_token/refresh_token — IF-004, имена — AuthCoreDefaults), чтение exp
/// access-JWT и инспекция записи refresh-токена в хранилище: хэш — SHA-256 hex
/// от UTF-8 значения (формат хранения IF-003/ITokenService.CreateRefreshToken).
/// </summary>
public static class B07AuthClients
{
    public const string RegisterEndpoint = "/api/v1/auth/register";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RefreshEndpoint = "/api/v1/auth/refresh";
    public const string LogoutEndpoint = "/api/v1/auth/logout";

    /// <summary>Клиент с cookie-контейнером (поток вход → logout → refresh).</summary>
    public static HttpClient CreateClient(B07WebAppFactory factory) => HostClients.Create(factory);

    /// <summary>Клиент БЕЗ cookie-контейнера (кейс сам управляет Cookie-заголовком).</summary>
    public static HttpClient CreateClientWithoutCookies(B07WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>Клиент с фиксированным транспортным IP (given «RemoteIpAddress=…»).</summary>
    public static HttpClient CreateClientWithIp(B07WebAppFactory factory, string ip)
    {
        var client = HostClients.Create(factory);
        client.DefaultRequestHeaders.Add(B07TestClientIpResolver.TestIpHeader, ip);
        return client;
    }

    /// <summary>POST /auth/login {login, password} (сырой ответ — статус проверяет кейс).</summary>
    public static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginEndpoint, new { login, password });

    /// <summary>
    /// Разбирает Set-Cookie ответа в отображение «имя → значение» (до первого «;»).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadSetCookies(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                var pair = cookie.Split(';', 2)[0];
                var separator = pair.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                result[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
            }
        }

        return result;
    }

    /// <summary>Значение обязательной Set-Cookie по имени; отсутствие — ошибка сценария.</summary>
    public static string RequiredSetCookie(HttpResponseMessage response, string cookieName)
    {
        var cookies = ReadSetCookies(response);
        Assert.True(
            cookies.TryGetValue(cookieName, out var value) && value.Length > 0,
            $"В ответе ожидается Set-Cookie {cookieName}; фактически установлены: [{string.Join(", ", cookies.Keys)}].");
        return value;
    }

    /// <summary>Читает claim exp access-JWT (Unix-секунды) — сверка «exp больше прежнего».</summary>
    public static long ReadAccessJwtExp(string jwt)
    {
        var segments = jwt.Split('.');
        Assert.True(
            segments.Length == 3,
            $"Ожидался JWT из трёх сегментов, фактически: {segments.Length}.");
        using var document = JsonDocument.Parse(WebEncoders.Base64UrlDecode(segments[1]));
        Assert.True(
            document.RootElement.TryGetProperty("exp", out var exp) && exp.ValueKind == JsonValueKind.Number,
            "В payload access-JWT отсутствует числовой claim exp.");
        return exp.GetInt64();
    }

    /// <summary>
    /// Хэш хранения refresh-токена (IF-003): SHA-256 hex от UTF-8 значения,
    /// нижний регистр — дословно формат ITokenService.CreateRefreshToken.
    /// </summary>
    public static string RefreshTokenHash(string refreshTokenValue) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshTokenValue))).ToLowerInvariant();

    /// <summary>Запись refresh-токена в хранилище по значению cookie (инспекция IF-015).</summary>
    public static RefreshToken FindRefreshToken(B07WebAppFactory factory, string refreshTokenValue)
    {
        var token = factory.Services.GetRequiredService<ISecurityTokenRepository>()
            .FindLiveByHash(RefreshTokenHash(refreshTokenValue));
        Assert.NotNull(token);
        return token;
    }

    /// <summary>Сконфигурированный TTL refresh-токена, суток (Auth__RefreshTtlDays).</summary>
    public static int RefreshTtlDays(B07WebAppFactory factory) =>
        factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value.RefreshTtlDays;
}
