using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B14.Infrastructure;

/// <summary>
/// Предусловия и сессии тестового хоста батча B-14 — по переизданным формулировкам
/// кейсов (арбитраж a-017/CR-001):
///  - пользователи и группы создаются ПРЯМЫМ DI-сидом в IUserRepository/IGroupRepository
///    тестового хоста (методика NFR-001/FR-007/ADR-010); POST /auth/register и
///    POST /auth/login в предусловиях НЕ вызываются; пароль DI-сид-пользователя
///    хэшируется РЕАЛЬНЫМ IPasswordHasher хоста (IF-002) — кейсы FR-022 сверяют
///    «верный/неверный текущий пароль» против хранимого хэша;
///  - сессия — cookie access_token с JWT HS256, минтым ХАРНЕСОМ ключом Auth__JwtKey
///    тестового хоста: выпуск через ITokenService хоста (методика ADR-022) даёт ровно
///    контракт кейса — claims sub={uuid}, role, jti=uuid, iat=now,
///    exp=iat+Auth__AccessTtlMinutes, подпись HS256 ключом хоста;
///  - refresh-токены «устройств» кейса TS-097 минтся ITokenService.CreateRefreshToken
///    и кладутся в IRefreshTokenRepository (хранится только TokenHash, IF-003/IF-015)
///    — ровно то состояние, которое создаёт вход на устройстве; cookie передаются
///    заголовком Cookie ЯВНО на каждый запрос клиента (урок CR-002: CookieContainer
///    не возвращает Secure-cookie для http); refresh-cookie устройства используется
///    ТОЛЬКО на POST /auth/refresh (Path=/api/v1/auth, IF-004) и не предъявляется
///    на /me/password — модель SEC-006/ASM-020 сохранена.
/// Информация о пользователях для проверок хранилища — IUserRepository из
/// factory.Services (чтение возвращает копии-снимки; контракт IF-015).
/// </summary>
public static class B14Harness
{
    public const string ProfileEndpoint = "/api/v1/me/profile";
    public const string PasswordEndpoint = "/api/v1/me/password";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RefreshEndpoint = "/api/v1/auth/refresh";

    /// <summary>Пароль DI-сид-пользователей до смены (правила §8 соблюдены).</summary>
    public const string TestUserPassword = "Passw0rd!";

    /// <summary>Валидный новый пароль смены (правила §8 соблюдены).</summary>
    public const string NewPassword = "NewPass1!";

    /// <summary>
    /// Клиент без авто-редиректов (точные статусы) и без cookie-контейнера:
    /// носители сессии передаются заголовком явно (CR-002).
    /// </summary>
    public static HttpClient Create(B14WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// given «сессия пользователя role»: клиент с ЯВНЫМ заголовком Cookie
    /// access_token={минтированный access-JWT HS256 ключом Auth__JwtKey хоста}.
    /// </summary>
    public static HttpClient CreateSessionClient(B14WebAppFactory factory, Guid userId, string role)
    {
        // Минт харнесом: HS256, sub/jti/iat + role, exp = iat + Auth__AccessTtlMinutes
        // (ITokenService, IF-003); ключ — AuthOptions хоста, заданный фабрикой.
        var token = factory.Services
            .GetRequiredService<ITokenService>()
            .IssueAccessToken(userId, role);

        var client = Create(factory);
        // Имя cookie — константа Auth.Core (единый источник с ICookieService, ADR-022).
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={token}");
        return client;
    }

    /// <summary>
    /// given «действующий refresh-токен устройства»: ITokenService.CreateRefreshToken
    /// + запись в ISecurityTokenRepository (Id/TokenHash/ExpiresAt — как при выпуске
    /// входом; в хранилище только SHA-256-хэш, IF-003/IF-015). Возвращает ОТКРЫТОЕ
    /// значение токена для refresh-cookie устройства.
    /// </summary>
    public static string SeedRefreshToken(B14WebAppFactory factory, Guid userId)
    {
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        var grant = tokens.CreateRefreshToken(userId);

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
    /// given «пользователь role с паролем создан прямым DI-сидом в IUserRepository
    /// тестового хоста; uuid известен харнесу»: PasswordHash — реальный PBKDF2-хэш
    /// (IPasswordHasher.Hash хоста, IF-002), поэтому ветки FR-022
    /// «верный/неверный текущий» и «вход старым/новым паролем» сверяются с хэшем.
    /// </summary>
    public static User SeedUser(
        B14WebAppFactory factory,
        string login,
        string email,
        string fullName,
        string role,
        Guid? groupId,
        string password)
    {
        // IF-002 v2.2: Hash(password, caller) — DI-сид помечается меткой seed
        // (конвенция зон B-16/B-21); снимки Δkdf кейсы снимают ПОСЛЕ сида.
        var passwordHash = factory.Services
            .GetRequiredService<IPasswordHasher>()
            .Hash(password, KdfCallers.Seed);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = passwordHash,
            FullName = fullName,
            Role = role,
            GroupId = groupId,
            CreatedAt = DateTime.UtcNow,
        };

        factory.Services.GetRequiredService<IUserRepository>().Add(user);
        return user;
    }

    /// <summary>given «группа создана DI-сидом IGroupRepository».</summary>
    public static Group SeedGroup(B14WebAppFactory factory, string name)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTime.UtcNow,
        };

        factory.Services.GetRequiredService<IGroupRepository>().Add(group);
        return group;
    }

    /// <summary>
    /// given «сессия teacher … известна из сида» (TS-196): сид-преподаватель FR-004
    /// (сидится всегда, независимо от Seed__DemoData; login — умолчание
    /// SeedOptions.DefaultTeacherLogin, GroupId=null).
    /// </summary>
    public static User ResolveSeedTeacher(B14WebAppFactory factory)
    {
        var teacher = factory.Services.GetRequiredService<IUserRepository>()
            .GetByLogin(SeedOptions.DefaultTeacherLogin);
        Assert.True(
            teacher is not null,
            "Предусловие кейса: сид-преподаватель отсутствует в хранилище тестового хоста.");
        Assert.Equal(UserRoles.Teacher, teacher!.Role);
        Assert.Null(teacher.GroupId);
        return teacher;
    }

    /// <summary>Пользователь по email (ci) из хранилища тестового хоста (копия-снимок, IF-015).</summary>
    public static User UserByEmail(B14WebAppFactory factory, string email)
    {
        var user = factory.Services.GetRequiredService<IUserRepository>().GetByEmail(email);
        Assert.NotNull(user);
        return user!;
    }

    /// <summary>Шаг «вход с паролем»: POST /auth/login; статус возвращает сценарий на проверку.</summary>
    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginEndpoint, new { login, password });

    /// <summary>
    /// Шаг «POST /auth/refresh с refresh-cookie устройства»: явный заголовок Cookie
    /// refresh_token={значение} (Path=/api/v1/auth, IF-004); тело — как при refresh
    /// из браузера, тело запроса не требуется.
    /// </summary>
    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshTokenValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RefreshEndpoint) { Content = null };
        request.Headers.Add(
            "Cookie",
            $"{AuthCoreDefaults.RefreshTokenCookieName}={refreshTokenValue}");
        return client.SendAsync(request);
    }
}
