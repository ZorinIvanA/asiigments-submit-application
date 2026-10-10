using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B16.Infrastructure;

/// <summary>
/// Предусловия и сессии тестового хоста батча B-16 (методика волны, ADR-015/CR-001):
///  - пользователи и группы создаются ПРЯМЫМ DI-сидом в IUserRepository/IGroupRepository
///    тестового хоста; пароль DI-сид-пользователя хэшируется РЕАЛЬНЫМ IPasswordHasher
///    хоста (IF-002) — кейсы FR-015/FR-016 сверяют «верный/неверный/дословный текущий
///    пароль» против хранимого хэша; POST /auth/login и POST /auth/register в
///    предусловиях НЕ вызываются;
///  - сессия — cookie access_token с access-JWT, выпущенным ITokenService тестового
///    хоста (ADR-015: сквозные сессии доменных кейсов не зависят от POST /auth/login);
///  - refresh-токены «устройств» кейсов TS-085/TS-086 минтся ITokenService.CreateRefreshToken
///    и кладутся в ISecurityTokenRepository (консолидированное хранилище
///    refresh/recovery/reset, ADR-033; хранится только TokenHash, IF-003/IF-015) —
///    ровно то состояние, которое создаёт вход на устройстве; cookie передаются
///    заголовком Cookie ЯВНО на каждый запрос клиента (урок CR-002: CookieContainer
///    не возвращает Secure-cookie для http); refresh-cookie текущей сессии предъявляется
///    на PUT /me/password ТОЛЬКО там, где это задано кейсом (TS-085 — присутствует,
///    TS-086 — отсутствует).
/// Информация о пользователях для проверок хранилища — IUserRepository из
/// factory.Services (чтение возвращает копии-снимки; контракт IF-015).
/// </summary>
public static class B16Harness
{
    public const string ProfileEndpoint = "/api/v1/me/profile";
    public const string PasswordEndpoint = "/api/v1/me/password";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RefreshEndpoint = "/api/v1/auth/refresh";

    /// <summary>Пароль DI-сид-пользователей до смены (правила §8 соблюдены; значение
    /// демо-учёток — кейс TS-206 задаёт «текущий пароль student01 — 'student123!'»).</summary>
    public const string TestUserPassword = "student123!";

    /// <summary>Валидный новый пароль смены (буквально из when кейсов TS-093..TS-098).</summary>
    public const string NewPassword = "NewPass1!";

    /// <summary>
    /// Клиент без авто-редиректов (точные статусы) и без cookie-контейнера:
    /// носители сессии передаются заголовком явно (CR-002).
    /// </summary>
    public static HttpClient Create(B16WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// given «пользователь авторизован»: клиент с ЯВНЫМ заголовком Cookie
    /// access_token={минтованный access-JWT}; <paramref name="refreshTokenValue"/> —
    /// значение refresh-cookie текущей сессии (кейс TS-095: «refresh-cookie текущей
    /// сессии присутствует» — передаётся тем же заголовком).
    /// </summary>
    public static HttpClient CreateSessionClient(
        B16WebAppFactory factory,
        Guid userId,
        string role,
        string? refreshTokenValue = null)
    {
        // Минт сессии (ADR-015): access-JWT (userId, role) через ITokenService хоста.
        var jwt = factory.Services
            .GetRequiredService<ITokenService>()
            .IssueAccessToken(userId, role);

        var cookie = $"{AuthCoreDefaults.AccessTokenCookieName}={jwt}";
        if (refreshTokenValue is not null)
        {
            cookie += $"; {AuthCoreDefaults.RefreshTokenCookieName}={refreshTokenValue}";
        }

        var client = Create(factory);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    /// <summary>
    /// given «пользователь создан прямым DI-сидом в IUserRepository тестового хоста;
    /// uuid известен харнесу»: PasswordHash — реальный PBKDF2-хэш (IPasswordHasher.Hash
    /// хоста, IF-002: Hash(password, caller)), поэтому ветки FR-016 «верный/неверный/
    /// дословный текущий пароль» и «вход старым/новым паролем» сверяются с хэшем.
    /// Деривация сида помечается меткой вызывателя KdfCallers.Seed («seed», IF-002:
    /// «КАЖДАЯ деривация инкрементирует счётчик с меткой вызывателя»; CR-001 арности
    /// это не влияет — Δkdf-кейсы снимают счётчик ПОСЛЕ сида, считая дельты).
    /// </summary>
    public static User SeedUser(
        B16WebAppFactory factory,
        string login,
        string email,
        string fullName,
        string role,
        Guid? groupId,
        string password)
    {
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
    public static Group SeedGroup(B16WebAppFactory factory, string name)
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
    /// given «текущее состояние групп изменилось» (TS-080: «groupName
    /// пересчитан»): переименование группы прямым DI-сидом IGroupRepository.Update —
    /// ответ профиля должен показать НОВОЕ имя (вычисление по текущему состоянию
    /// групп, FR-015).
    /// </summary>
    public static void RenameGroup(B16WebAppFactory factory, Group group, string newName)
    {
        factory.Services.GetRequiredService<IGroupRepository>().Update(new Group
        {
            Id = group.Id,
            Name = newName,
            CreatedAt = group.CreatedAt,
        });
    }

    /// <summary>
    /// given «действующий refresh-токен устройства»: ITokenService.CreateRefreshToken
    /// + запись в ISecurityTokenRepository (Id/TokenHash/ExpiresAt — как при выпуске
    /// входом; в хранилище только SHA-256-хэш, IF-003/IF-015). Возвращает ОТКРЫТОЕ
    /// значение токена для refresh-cookie устройства.
    /// </summary>
    public static string SeedRefreshToken(B16WebAppFactory factory, Guid userId)
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

    /// <summary>Пользователь по uuid из хранилища тестового хоста (копия-снимок, IF-015).</summary>
    public static User StoredUser(B16WebAppFactory factory, Guid userId)
    {
        var user = factory.Services.GetRequiredService<IUserRepository>().GetById(userId);
        Assert.NotNull(user);
        return user!;
    }

    /// <summary>Шаг «вход с паролем»: POST /auth/login; статус возвращает сценарий на проверку.</summary>
    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginEndpoint, new { login, password });

    /// <summary>
    /// Шаг «POST /auth/refresh с refresh-cookie устройства»: явный заголовок Cookie
    /// refresh_token={значение}; тело запроса не требуется (как при refresh из браузера).
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
