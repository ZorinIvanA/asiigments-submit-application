using System.Globalization;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>
/// Помощники кейсов scope-волны батча B-13 (TS-195, TS-197): DI-сид студента,
/// минт access-cookie через ITokenService (ADR-022 — POST /auth/login не
/// используется) и минт refresh-сессии (CreateRefreshToken + запись хэша в
/// ISecurityTokenRepository — консолидированное токенное хранилище T-101, IF-015).
/// Работают поверх любого хоста <see cref="WebApplicationFactory{Program}"/>
/// (и B13WebAppFactory, и B13ScopeWebAppFactory). Клиент — без авто-редиректов
/// и без cookie-контейнера: состав cookie — часть given/when кейсов.
/// </summary>
public static class B13ScopeHost
{
    public const string RefreshEndpoint = "/api/v1/auth/refresh";
    public const string MeEndpoint = "/api/v1/auth/me";
    public const string StudentsEndpoint = "/api/v1/students";

    /// <summary>Клиент тестового хоста: точные статусы, cookie переносит сам тест.</summary>
    public static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// DI-сид студента напрямую в IUserRepository (ADR-010; сессии минтятся —
    /// пароль не используется).
    /// </summary>
    public static User AddStudent(WebApplicationFactory<Program> factory, string login)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = string.Create(CultureInfo.InvariantCulture, $"{login}@example.com"),
            PasswordHash = "di-seed-mint-only",
            FullName = $"Студент {login}",
            Role = UserRoles.Student,
            GroupId = null,
            CreatedAt = DateTime.UtcNow,
        };
        factory.Services.GetRequiredService<IUserRepository>().Add(user);
        return user;
    }

    /// <summary>Минт сессии (ADR-022): access-JWT (userId, role) заголовком Cookie.</summary>
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

    /// <summary>Клиент с сессией сид-преподавателя (учётка создана сидом FR-004).</summary>
    public static HttpClient CreateTeacherClient(WebApplicationFactory<Program> factory)
    {
        var teacher = factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException(
                "Сид-преподаватель не найден — шаг given «сессия teacher» неисполним.");
        var client = CreateClient(factory);
        MintAccessCookie(factory, client, teacher.Id, UserRoles.Teacher);
        return client;
    }

    /// <summary>
    /// Минт refresh-сессии (given «пользователь с валидным refresh-токеном»):
    /// CreateRefreshToken + запись хэша в ISecurityTokenRepository; возвращает
    /// grant (значение cookie и TokenHash — для инспекции хранилища в тестах).
    /// </summary>
    public static RefreshTokenGrant EstablishRefreshSession(WebApplicationFactory<Program> factory, Guid userId)
    {
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        var refreshTokens = factory.Services.GetRequiredService<ISecurityTokenRepository>();
        var grant = tokens.CreateRefreshToken(userId);
        refreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = grant.TokenHash,
            ExpiresAt = grant.ExpiresAt,
            CreatedAt = DateTime.UtcNow,
        });
        return grant;
    }

    /// <summary>
    /// ЖИВАЯ запись хранилища по хэшу выданного grant (точное сравнение Ordinal)
    /// либо null: отозванный/истёкший токен не жив. Для кейса TS-197 чувствительность
    /// сохранена: ротация/отзыв исходного токена → null → проверка NotNull падает.
    /// </summary>
    public static RefreshToken? FindStoredRefreshToken(
        WebApplicationFactory<Program> factory,
        RefreshTokenGrant grant) =>
        factory.Services.GetRequiredService<ISecurityTokenRepository>().FindLiveByHash(grant.TokenHash);

    /// <summary>
    /// POST /auth/refresh с ЯВНО заданной refresh-cookie (сырое значение в заголовке
    /// запроса — контейнера нет, состав cookie полностью под контролем теста).
    /// </summary>
    public static Task<HttpResponseMessage> PostRefreshWithRawCookieAsync(
        HttpClient client,
        string refreshTokenValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RefreshEndpoint);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            $"{AuthCoreDefaults.RefreshTokenCookieName}={refreshTokenValue}");
        return client.SendAsync(request);
    }

    /// <summary>Имена cookie из всех Set-Cookie заголовков ответа (значение — до первого '=').</summary>
    public static IReadOnlyList<string> ReadSetCookieNames(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return Array.Empty<string>();
        }

        return values
            .Select(value => value.Split('=', 2)[0].Trim())
            .ToList();
    }
}
