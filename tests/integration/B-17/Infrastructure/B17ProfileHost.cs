using System.Net.Http.Json;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B17.Infrastructure;

/// <summary>
/// Предусловия и сессии доменной зоны профиля/пароля батча B-17 (кейсы
/// TS-079..TS-087, TS-170, TS-171, TS-176 — FR-015/FR-016; методика волны,
/// ADR-015/CR-001; файл зоны — собственная копия механики харнеса B-16,
/// чужие зоны недоступны для ссылок — BL-001):
///  - пользователи и группы создаются ПРЯМЫМ DI-сидом в IUserRepository/
///    IGroupRepository тестового хоста; пароль DI-сид-пользователя хэшируется
///    РЕАЛЬНЫМ IPasswordHasher хоста (IF-002) — кейсы FR-015/FR-016 сверяют
///    «верный/неверный/дословный текущий пароль» против хранимого хэша;
///    POST /auth/login и POST /auth/register в предусловиях НЕ вызываются;
///  - сессия — cookie access_token с access-JWT, выпущенным ITokenService
///    тестового хоста (ADR-015);
///  - refresh-токены «устройств» кейсов TS-085/TS-086 минтся
///    ITokenService.CreateRefreshToken и кладутся в ISecurityTokenRepository
///    (консолидированное хранилище refresh/recovery/reset, IF-015; хранится
///    только TokenHash) — cookie передаются заголовком Cookie ЯВНО на каждый
///    запрос (урок CR-002: CookieContainer не возвращает Secure-cookie для http);
///  - Δkdf-гейты кейсов TS-083/TS-084/TS-085/TS-171 — дельты снимков
///    IKdfCounter.Snapshot() (IF-002/ADR-031, типизированный DI-резолв: зона
///    B-17 и так ссылается на типы LabsApp напрямую — B17Host).
/// </summary>
public static class B17ProfileHost
{
    // Контракты IF-013 (эндпоинты кейсов зоны профиля/пароля).
    public const string ProfileEndpoint = "/api/v1/me/profile";
    public const string PasswordEndpoint = "/api/v1/me/password";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RefreshEndpoint = "/api/v1/auth/refresh";

    /// <summary>Пароль DI-сид-пользователей до смены (правилам §8 удовлетворяет).</summary>
    public const string TestUserPassword = "student123!";

    /// <summary>Валидный новый пароль смены (дословно из when кейсов).</summary>
    public const string NewPassword = "NewPass1!";

    /// <summary>
    /// Клиент без авто-редиректов (точные статусы) и без cookie-контейнера:
    /// носители сессии передаются заголовком явно (CR-002).
    /// </summary>
    public static HttpClient Create(B17WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// given «пользователь авторизован»: клиент с ЯВНЫМ заголовком Cookie
    /// access_token={минтованный access-JWT}; <paramref name="refreshTokenValue"/> —
    /// значение refresh-cookie текущей сессии (кейс TS-085: «refresh-cookie
    /// текущей сессии присутствует» — передаётся тем же заголовком).
    /// </summary>
    public static HttpClient CreateSessionClient(
        B17WebAppFactory factory,
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
    /// given «пользователь создан прямым DI-сидом в IUserRepository тестового
    /// хоста»: PasswordHash — реальный PBKDF2-хэш (IPasswordHasher.Hash хоста,
    /// IF-002), поэтому ветки FR-016 «верный/неверный/дословный текущий пароль»
    /// и «вход старым/новым паролем» сверяются с хэшем. Деривация сида помечена
    /// меткой KdfCallers.Seed; Δkdf-кейсы снимают счётчик ПОСЛЕ сида, считая
    /// дельты вокруг измеряемого запроса.
    /// </summary>
    public static User SeedUser(
        B17WebAppFactory factory,
        string login,
        string email,
        string fullName,
        string role,
        Guid? groupId,
        string password)
    {
        var clock = factory.Services.GetRequiredService<TimeProvider>();
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
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };

        factory.Services.GetRequiredService<IUserRepository>().Add(user);
        return user;
    }

    /// <summary>given «группа создана DI-сидом IGroupRepository» (кейсы TS-079/TS-080).</summary>
    public static Group SeedGroup(B17WebAppFactory factory, string name)
    {
        var clock = factory.Services.GetRequiredService<TimeProvider>();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };

        factory.Services.GetRequiredService<IGroupRepository>().Add(group);
        return group;
    }

    /// <summary>
    /// given «после обновления группа пользователя переименована» (TS-080:
    /// «groupName пересчитан»): переименование прямым DI-сидом
    /// IGroupRepository.Update — ответ профиля должен показать НОВОЕ имя
    /// (вычисление по текущему состоянию групп, FR-015).
    /// </summary>
    public static void RenameGroup(B17WebAppFactory factory, Group group, string newName)
    {
        factory.Services.GetRequiredService<IGroupRepository>().Update(new Group
        {
            Id = group.Id,
            Name = newName,
            CreatedAt = group.CreatedAt,
        });
    }

    /// <summary>
    /// given «действующий refresh-токен устройства» (TS-085/TS-086):
    /// ITokenService.CreateRefreshToken + запись в ISecurityTokenRepository
    /// (Id/TokenHash/ExpiresAt — как при выпуске входом; в хранилище только
    /// SHA-256-хэш, IF-003/IF-015). Возвращает ОТКРЫТОЕ значение токена для
    /// refresh-cookie устройства.
    /// </summary>
    public static string SeedRefreshToken(B17WebAppFactory factory, Guid userId)
    {
        var clock = factory.Services.GetRequiredService<TimeProvider>();
        var grant = factory.Services.GetRequiredService<ITokenService>().CreateRefreshToken(userId);

        factory.Services.GetRequiredService<ISecurityTokenRepository>().Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = grant.TokenHash,
            ExpiresAt = grant.ExpiresAt,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        });

        return grant.Value;
    }

    /// <summary>Пользователь по uuid из хранилища тестового хоста (копия-снимок, IF-015).</summary>
    public static User StoredUser(B17WebAppFactory factory, Guid userId)
    {
        var user = factory.Services.GetRequiredService<IUserRepository>().GetById(userId);
        Assert.NotNull(user);
        return user!;
    }

    /// <summary>Шаг «вход с паролем»: POST /auth/login; статус возвращает сценарий на проверку.</summary>
    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginEndpoint, new { login, password });

    /// <summary>
    /// Шаг «POST /auth/refresh с refresh-cookie устройства»: явный заголовок
    /// Cookie refresh_token={значение}; тело запроса не требуется (как при
    /// refresh из браузера).
    /// </summary>
    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshTokenValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RefreshEndpoint) { Content = null };
        request.Headers.Add(
            "Cookie",
            $"{AuthCoreDefaults.RefreshTokenCookieName}={refreshTokenValue}");
        return client.SendAsync(request);
    }

    /// <summary>
    /// Снимок тестового шва счётчика KDF (IF-002/ADR-031: IKdfCounter.Snapshot(),
    /// DI-регистрация — AddAuthCore): словарь «метка вызывателя → число дериваций
    /// с момента старта хоста». «Сброс счётчика» из given кейсов реализуется
    /// ДЕЛЬТАМИ: снимок до и снимок после измеряемого участка.
    /// </summary>
    public static IReadOnlyDictionary<string, long> KdfSnapshot(B17WebAppFactory factory) =>
        factory.Services.GetRequiredService<IKdfCounter>().Snapshot();

    /// <summary>Δkdf суммарно по всем меткам между снимками (кейс: «Δkdf=…»).</summary>
    public static long KdfTotalDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after) =>
        after.Values.Sum() - before.Values.Sum();

    /// <summary>
    /// Δkdf по ОДНОЙ метке вызывателя между снимками (кейсы TS-083/TS-171:
    /// ровно одна деривация Verify текущего пароля под меткой change_password —
    /// валидация/хэширование нового не выполнялись).
    /// </summary>
    public static long KdfCallerDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after,
        string caller) =>
        (after.TryGetValue(caller, out var afterValue) ? afterValue : 0) -
        (before.TryGetValue(caller, out var beforeValue) ? beforeValue : 0);
}
