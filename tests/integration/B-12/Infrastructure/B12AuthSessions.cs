using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Infrastructure;

/// <summary>
/// Маршруты auth-эндпойнтов кейсов TS-057..TS-067/TS-156..TS-159 (IF-007,
/// СПО /api/v1) — единый источник путей в зоне B-12.
/// </summary>
public static class B12AuthEndpoints
{
    public const string Refresh = "/api/v1/auth/refresh";
    public const string Logout = "/api/v1/auth/logout";
    public const string Me = "/api/v1/auth/me";
    public const string Labs = "/api/v1/labs";
}

/// <summary>
/// Помощники auth-сценариев батча B-12 (TS-057..TS-067, TS-156..TS-159):
/// сессии минтятся через DI тестового хоста (ADR-015/ADR-022 — минт access-JWT
/// через ITokenService, DI-запись refresh-токена через ITokenService.CreateRefreshToken
/// + ISecurityTokenRepository.Add), разбор Set-Cookie, инспекция записи refresh-токена
/// в хранилище (хэш — SHA-256 hex от UTF-8 значения, формат хранения IF-003).
/// Обобщено по WebApplicationFactory&lt;Program&gt; — хосты B12WebAppFactory и
/// B12FakeTimeWebAppFactory равноправны. POST /auth/login не используется.
/// Адаптация к консолидации хранилищ T-101 (IF-015): прежний
/// IRefreshTokenRepository.FindByHash заменён контрактом
/// ISecurityTokenRepository (Add / FindLiveByHash); для инспекции ОТЗЫВА
/// (RevokedAt записи, кейсы TS-060/TS-061/TS-062) служит отдельный шов
/// FindStoredTokenIncludingRevoked — консолидированный интерфейс отозванную
/// запись наружу не отдаёт.
/// </summary>
public static class B12AuthSessions
{
    /// <summary>Клиент без авто-редиректов (точные статусы) и без cookie-контейнера.</summary>
    public static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// Клиент с минтованной access-cookie (ADR-022): access-JWT (userId, role)
    /// через ITokenService хоста, имя cookie — константа Auth.Core.
    /// </summary>
    public static HttpClient CreateClientWithAccess(
        WebApplicationFactory<Program> factory,
        Guid userId,
        string role)
    {
        var client = CreateClient(factory);
        MintAccessCookie(factory, client, userId, role);
        return client;
    }

    /// <summary>Минт access-cookie в DefaultRequestHeaders клиента (ADR-022).</summary>
    public static void MintAccessCookie(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        Guid userId,
        string role)
    {
        var jwt = factory.Services.GetRequiredService<ITokenService>().IssueAccessToken(userId, role);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={jwt}");
    }

    /// <summary>
    /// DI-минт живого refresh-токена пользователя: ITokenService.CreateRefreshToken
    /// (значение + SHA-256-хэш + expiresAt по TimeProvider) и запись в
    /// ISecurityTokenRepository. Возвращает ОТКРЫТОЕ значение для cookie.
    /// </summary>
    public static string CreateLiveRefreshToken(WebApplicationFactory<Program> factory, Guid userId)
    {
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        var repository = factory.Services.GetRequiredService<ISecurityTokenRepository>();
        var now = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var grant = tokens.CreateRefreshToken(userId);
        repository.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = grant.TokenHash,
            ExpiresAt = grant.ExpiresAt,
            CreatedAt = now,
        });
        return grant.Value;
    }

    /// <summary>Хэш хранения refresh-токена (IF-003): SHA-256 hex от UTF-8 значения.</summary>
    public static string RefreshTokenHash(string refreshTokenValue) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshTokenValue))).ToLowerInvariant();

    /// <summary>
    /// ЖИВАЯ запись refresh-токена в хранилище по значению cookie (инспекция IF-015
    /// через контракт консолидированного хранилища ISecurityTokenRepository);
    /// отозванная/истёкшая/отсутствующая запись контракт живым не считает — null.
    /// </summary>
    public static RefreshToken? FindStoredToken(WebApplicationFactory<Program> factory, string refreshTokenValue) =>
        factory.Services.GetRequiredService<ISecurityTokenRepository>().FindLiveByHash(RefreshTokenHash(refreshTokenValue));

    /// <summary>Запись обязательна живой (иначе инспекция given невозможна) — снимок сущности.</summary>
    public static RefreshToken RequireStoredToken(WebApplicationFactory<Program> factory, string refreshTokenValue) =>
        FindStoredToken(factory, refreshTokenValue)
        ?? throw new InvalidOperationException(
            "Запись refresh-токена не найдена в хранилище — шаг given/then неисполним.");

    /// <summary>
    /// Запись refresh-токена НЕЗАВИСИМО от живости (инспекция факта отзыва RevokedAt —
    /// кейсы TS-060/TS-061/TS-062): консолидированный контракт ISecurityTokenRepository
    /// (T-101, IF-015) отозванную запись наружу не отдаёт (FindLiveByHash — только живые),
    /// поэтому шов читает внутренний словарь DI-singleton InMemorySecurityTokenRepository
    /// рефлексией. Изменение внутренней формы реализации — громкое падение с диагностикой,
    /// а не молчаливый исход.
    /// </summary>
    public static RefreshToken? FindStoredTokenIncludingRevoked(
        WebApplicationFactory<Program> factory,
        string refreshTokenValue)
    {
        var repository = factory.Services.GetRequiredService<ISecurityTokenRepository>();
        var tokenHash = RefreshTokenHash(refreshTokenValue);
        const string InternalTokensFieldName = "_refreshTokens";
        var tokensField = repository.GetType().GetField(
                InternalTokensFieldName,
                BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"Шов инспекции отзыва неисполним: у {repository.GetType().Name} нет поля "
                + $"<{InternalTokensFieldName}> — внутренняя форма хранилища изменилась, "
                + "инспекция RevokedAt требует обновления шва зоны B-12.");
        var tokensMap = (System.Collections.IDictionary)(tokensField.GetValue(repository)
            ?? throw new InvalidOperationException(
                $"Шов инспекции отзыва неисполним: поле <{InternalTokensFieldName}> равно null."));
        foreach (var record in tokensMap.Values)
        {
            var token = (RefreshToken)record;
            if (string.Equals(token.TokenHash, tokenHash, StringComparison.Ordinal))
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>
    /// Запись независимо от живости обязательна (иначе инспекция then невозможна) —
    /// запись хранилища с актуальным RevokedAt.
    /// </summary>
    public static RefreshToken RequireStoredTokenIncludingRevoked(
        WebApplicationFactory<Program> factory,
        string refreshTokenValue) =>
        FindStoredTokenIncludingRevoked(factory, refreshTokenValue)
        ?? throw new InvalidOperationException(
            "Запись refresh-токена не найдена в хранилище — шаг then неисполним.");

    /// <summary>POST-запрос с одним cookie-заголовком (кейс сам управляет cookie).</summary>
    public static HttpRequestMessage PostWithCookie(string endpoint, string cookieHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        return request;
    }

    /// <summary>POST /auth/refresh с refresh-cookie (запрос без тела, IF-007).</summary>
    public static HttpRequestMessage CreateRefreshRequest(string refreshTokenValue) =>
        PostWithCookie(B12AuthEndpoints.Refresh, CookieHeader(AuthCoreDefaults.RefreshTokenCookieName, refreshTokenValue));

    /// <summary>POST /auth/logout с парой cookie access+refresh (шаг given «пользователь вошёл»).</summary>
    public static HttpRequestMessage CreateLogoutRequest(string? accessToken = null, string? refreshToken = null)
    {
        var pairs = new List<string>();
        if (accessToken is not null)
        {
            pairs.Add(CookiePair(AuthCoreDefaults.AccessTokenCookieName, accessToken));
        }

        if (refreshToken is not null)
        {
            pairs.Add(CookiePair(AuthCoreDefaults.RefreshTokenCookieName, refreshToken));
        }

        return PostWithCookie(B12AuthEndpoints.Logout, string.Join("; ", pairs));
    }

    /// <summary>Заголовок Cookie из одной пары имя=значение.</summary>
    public static string CookieHeader(string name, string value) => CookiePair(name, value);

    private static string CookiePair(string name, string value) => $"{name}={value}";

    /// <summary>Все заголовки Set-Cookie ответа (сырые строки).</summary>
    public static IReadOnlyList<string> SetCookieHeaders(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToArray()
            : Array.Empty<string>();

    /// <summary>
    /// Первый Set-Cookie с указанным именем cookie (сырая строка с атрибутами) —
    /// для проверок «Max-Age=0» / «cookie переустановлен».
    /// </summary>
    public static bool TryGetSetCookie(HttpResponseMessage response, string cookieName, out string? rawCookie)
    {
        foreach (var raw in SetCookieHeaders(response))
        {
            var pair = raw.Split(';', 2)[0];
            var separator = pair.IndexOf('=');
            if (separator > 0 && pair[..separator].Trim() == cookieName)
            {
                rawCookie = raw;
                return true;
            }
        }

        rawCookie = null;
        return false;
    }
}
