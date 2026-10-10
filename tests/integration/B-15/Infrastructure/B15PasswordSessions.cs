using System.Globalization;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B15.Infrastructure;

/// <summary>
/// Фикстура кейсов смены пароля (TS-083..TS-087, FR-016): Development-хост B-15
/// с Auth__Pbkdf2Iterations=1000 (IF-002: «умолчание 210000, тесты 1000») —
/// кейсы выполняют реальные деривации Verify/Hash, и гейты Δkdf (FR-016/IF-013)
/// должны идти быстро и детерминированно. Остальные настройки наследуются от
/// <see cref="B15WebAppFactory"/> (Auth__JwtKey, Seed__DemoData=false, log-sink).
/// </summary>
public sealed class B15PasswordWebAppFactory : B15WebAppFactory
{
    /// <summary>Тестовое число итераций PBKDF2 (IF-002: «тесты 1000»).</summary>
    public const int TestPbkdf2Iterations = 1000;

    public B15PasswordWebAppFactory()
        : base(
            Environments.Development,
            new Dictionary<string, string?>
            {
                [AuthOptions.Pbkdf2IterationsVariable] =
                    TestPbkdf2Iterations.ToString(CultureInfo.InvariantCulture),
            })
    {
    }
}

/// <summary>
/// Предусловия и сессии кейсов смены пароля (TS-083..TS-087, FR-016) поверх
/// методики зоны (B15Harness, ADR-015/CR-001: POST /auth/login в предусловиях
/// НЕ используется):
///  - пользователь с РЕАЛЬНЫМ паролем создаётся DI-сидом: passwordHash —
///    IPasswordHasher.Hash(password, KdfCallers.Seed) тестового хоста (ровно одна
///    деривация с меткой seed — допустимо, Δkdf кейсов считается по метке
///    change_password между снимками B15KdfProbe); сессия минтится access-JWT
///    через ITokenService (IF-003);
///  - refresh-токены «устройств» (TS-085/TS-086) минтятся через
///    ITokenService.CreateRefreshToken + ISecurityTokenRepository.Add (зеркало
///    AuthController.IssueSession по публичным интерфейсам IF-003/IF-015, образец —
///    B10AuthSessions зоны B-10); исходное значение возвращается вызывающему —
///    оно же значение refresh-cookie;
///  - cookie передаются заголовком Cookie ЯВНО (урок CR-002 зоны: HandleCookies
///    отключён, Secure-cookie недоступны для http) — сессия с доступом несёт
///    access_token и, когда кейс требует, refresh_token текущего запроса.
/// </summary>
public static class B15PasswordSessions
{
    /// <summary>PUT /api/v1/me/password (FR-016).</summary>
    public const string MePasswordEndpoint = "/api/v1/me/password";

    /// <summary>POST /api/v1/auth/refresh (FR-009) — проверка живости refresh-токена.</summary>
    public const string RefreshEndpoint = "/api/v1/auth/refresh";

    /// <summary>POST /api/v1/auth/login (FR-007) — проверка входа по новому/старому паролю.</summary>
    public const string LoginEndpoint = "/api/v1/auth/login";

    /// <summary>
    /// given «пользователь с известным паролем»: DI-сид в IUserRepository с хэшем
    /// РЕАЛЬНОГО пароля (IPasswordHasher.Hash, метка seed) — Verify кейсов
    /// TS-083..TS-087 проходит/отказывает на подлинном хэше.
    /// </summary>
    public static User SeedUserWithPassword(
        B15WebAppFactory factory,
        string login,
        string email,
        string fullName,
        string password,
        Guid? groupId = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = factory.Services.GetRequiredService<IPasswordHasher>()
                .Hash(password, KdfCallers.Seed),
            FullName = fullName,
            Role = UserRoles.Student,
            GroupId = groupId,
            CreatedAt = DateTime.UtcNow,
        };

        factory.Services.GetRequiredService<IUserRepository>().Add(user);
        return user;
    }

    /// <summary>
    /// given «refresh-токен устройства»: выпуск через ITokenService (IF-003) и
    /// вставка в ISecurityTokenRepository (IF-015) — в хранилище только SHA-256-хэш;
    /// исходное значение (значение refresh-cookie) возвращается вызывающему.
    /// </summary>
    public static string MintRefreshToken(B15WebAppFactory factory, Guid userId)
    {
        var grant = factory.Services.GetRequiredService<ITokenService>().CreateRefreshToken(userId);
        factory.Services.GetRequiredService<ISecurityTokenRepository>().Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = grant.TokenHash,
            ExpiresAt = grant.ExpiresAt,
            CreatedAt = DateTime.UtcNow,
        });
        return grant.Value;
    }

    /// <summary>
    /// Клиент-сессия пользователя: заголовок Cookie access_token={минтированный
    /// access-JWT}; при непустом refreshToken добавляется refresh_token={значение}
    /// — cookie текущего запроса для арбитража ISS-002 (TS-085).
    /// </summary>
    public static HttpClient CreateSessionClient(
        B15WebAppFactory factory,
        Guid userId,
        string? refreshToken = null)
    {
        var accessJwt = factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(userId, UserRoles.Student);

        var cookie = $"{AuthCoreDefaults.AccessTokenCookieName}={accessJwt}";
        if (!string.IsNullOrEmpty(refreshToken))
        {
            cookie += $"; {AuthCoreDefaults.RefreshTokenCookieName}={refreshToken}";
        }

        return CreateClientWithCookieHeader(factory, cookie);
    }

    /// <summary>
    /// Клиент «второго устройства»/проверки refresh-cookie: заголовок Cookie
    /// содержит ТОЛЬКО refresh_token (без access) — для POST /auth/refresh.
    /// </summary>
    public static HttpClient CreateRefreshOnlyClient(
        B15WebAppFactory factory,
        string refreshToken) =>
        CreateClientWithCookieHeader(
            factory,
            $"{AuthCoreDefaults.RefreshTokenCookieName}={refreshToken}");

    private static HttpClient CreateClientWithCookieHeader(
        B15WebAppFactory factory,
        string cookieHeader)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", cookieHeader);
        return client;
    }
}
