using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Infrastructure;

/// <summary>
/// Клиент и предусловия тестового хоста батча B-08. Клиент — без авто-редиректов
/// (точные статусы) и БЕЗ автоматического CookieContainer: состав cookie — часть
/// given/when кейсов TS-061..TS-065, поэтому cookie переносит ручной
/// <see cref="B08CookieContainer"/>.
///
/// given «вход выполнен / сессия пользователя …» (TS-061..TS-067, TS-071):
/// DI-сид пользователя (IUserRepository + IPasswordHasher — ADR-010) и МИНТ
/// сессии: ITokenService.IssueAccessToken + CreateRefreshToken с записью хэша в
/// IRefreshTokenRepository, cookie выставляются в контейнер по константам
/// AuthCoreDefaults (единый источник имён/Path с ICookieService). POST
/// /auth/login для этого НЕ используется — эндпоинт принадлежит другой подзадаче
/// волны (ISS-002, ADR-022).
///
/// TS-062 («JWT с exp в прошлом»): доступ к механизму FakeTimeProvider зона
/// сознательно не получает — просроченный access-JWT минтится напрямую той же
/// машиной JwtSecurityToken с ключом фикстуры (детерминированно, без новых
/// пакетов и перевода времени хоста).
/// </summary>
public static class B08Host
{
    // Контракты IF-007/IF-008/FR-043 (эндпоинты кейсов батча).
    public const string LogoutEndpoint = "/api/v1/auth/logout";
    public const string RefreshEndpoint = "/api/v1/auth/refresh";
    public const string MeEndpoint = "/api/v1/auth/me";
    public const string RecoveryRequestEndpoint = "/api/v1/auth/recovery/request";
    public const string RecoveryConfirmEndpoint = "/api/v1/auth/recovery/confirm";

    /// <summary>Базовый префикс групп (FR-043: DELETE /groups/{id}).</summary>
    public const string GroupsEndpointPrefix = "/api/v1/groups/";

    /// <summary>Пароль всех сид-пользователей батча (правилам §8 удовлетворяет; сид идёт напрямую в хэш).</summary>
    public const string TestUserPassword = "Passw0rd!";

    /// <summary>
    /// Тексты ошибок контракта (IF-001/IF-008, дословно; констант в словаре
    /// ErrorTexts хостинга пока нет — расширяется другой подзадачей).
    /// </summary>
    public const string RateLimitedMessage = "Слишком много попыток. Повторите позже";
    public const string CodeRejectedMessage = "Код восстановления не подходит";

    /// <summary>Клиент батча: тест управляет составом cookie через ручной контейнер.</summary>
    public static Client CreateClient(B08WebAppFactory factory) => new(factory);

    /// <summary>Клиент батча: тест управляет составом cookie через ручной контейнер.</summary>
    public sealed class Client : IDisposable
    {
        private readonly HttpClient _http;

        public Client(B08WebAppFactory factory)
        {
            _http = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            });
        }

        /// <summary>Ручной cookie-контейнер клиента (единственный источник cookie запросов).</summary>
        public B08CookieContainer Cookies { get; } = new();

        /// <summary>Отправляет запрос: контейнер дополняет Cookie-заголовок, ответ — источник Set-Cookie.</summary>
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            Cookies.ApplyTo(request);
            var response = await _http.SendAsync(request);
            Cookies.CaptureFrom(response);
            return response;
        }

        public Task<HttpResponseMessage> GetAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            return SendAsync(request);
        }

        /// <summary>POST с JSON-телом (сырая строка — кейсы требуют точных тел, включая нестрочные поля).</summary>
        public Task<HttpResponseMessage> PostAsync(string url, string? json)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (json is not null)
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return SendAsync(request);
        }

        public Task<HttpResponseMessage> DeleteAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            return SendAsync(request);
        }

        public void Dispose() => _http.Dispose();
    }

    /// <summary>DI-сид пользователя с реальным PBKDF2-хэшем пароля (IF-002).</summary>
    public static User SeedUser(
        B08WebAppFactory factory,
        string login,
        string email,
        string fullName,
        string role,
        Guid? groupId = null)
    {
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        var clock = factory.Services.GetRequiredService<TimeProvider>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            // IPasswordHasher.Hash требует метку вызывателя (FR-005/ADR-007:
            // каждая деривация инкрементирует IKdfCounter с меткой; DI-сид —
            // метка seed, как у штатного сид-пути).
            PasswordHash = hasher.Hash(TestUserPassword, KdfCallers.Seed),
            FullName = fullName,
            Role = role,
            GroupId = groupId,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        users.Add(user);
        return user;
    }

    /// <summary>DI-сид студента (роль student).</summary>
    public static User SeedStudent(B08WebAppFactory factory, string login, string email, string fullName, Guid? groupId = null) =>
        SeedUser(factory, login, email, fullName, UserRoles.Student, groupId);

    /// <summary>DI-сид преподавателя (роль teacher).</summary>
    public static User SeedTeacher(B08WebAppFactory factory, string login, string email, string fullName) =>
        SeedUser(factory, login, email, fullName, UserRoles.Teacher);

    /// <summary>DI-сид группы (IGroupRepository.Add).</summary>
    public static Group SeedGroup(B08WebAppFactory factory, string name)
    {
        var groups = factory.Services.GetRequiredService<IGroupRepository>();
        var clock = factory.Services.GetRequiredService<TimeProvider>();

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        groups.Add(group);
        return group;
    }

    /// <summary>
    /// Минт сессии (ADR-022): access-cookie в контейнер клиента; refresh —
    /// CreateRefreshToken + запись хэша в ISecurityTokenRepository
    /// (консолидированное хранилище токенов, IF-015); возвращает
    /// grant (значение cookie и TokenHash — для инспекции отзыва в тестах).
    /// </summary>
    public static RefreshTokenGrant EstablishSession(B08WebAppFactory factory, Client client, Guid userId, string role)
    {
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        var refreshTokens = factory.Services.GetRequiredService<ISecurityTokenRepository>();
        var clock = factory.Services.GetRequiredService<TimeProvider>();

        client.Cookies.Set(AuthCoreDefaults.AccessTokenCookieName, tokens.IssueAccessToken(userId, role));

        var grant = tokens.CreateRefreshToken(userId);
        refreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = grant.TokenHash,
            ExpiresAt = grant.ExpiresAt,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        });
        client.Cookies.Set(AuthCoreDefaults.RefreshTokenCookieName, grant.Value);
        return grant;
    }

    /// <summary>
    /// Минт ПРОСРОЧЕННОГО access-JWT (given TS-062 «JWT с exp в прошлом»): те же
    /// claims и подпись HS256 ключом фикстуры, что у JwtTokenService, но
    /// exp на час в прошлом. Валидатор хоста такой токен отвергает.
    /// </summary>
    public static string MintExpiredAccessToken(B08WebAppFactory factory, Guid userId, string role)
    {
        var clock = factory.Services.GetRequiredService<TimeProvider>();
        var now = clock.GetUtcNow();

        var jwt = new JwtSecurityToken(
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
                new Claim(
                    JwtRegisteredClaimNames.Iat,
                    now.AddHours(-2).ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64),
                new Claim(AuthCoreDefaults.RoleClaimType, role),
            ],
            notBefore: now.AddHours(-2).UtcDateTime,
            expires: now.AddHours(-1).UtcDateTime,
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(B08WebAppFactory.TestJwtKey)),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    /// <summary>
    /// Запись refresh-токена по хэшу из хранилища тестового хоста (только
    /// чтение, для then «refresh помечен revokedAt» кейсов TS-061/TS-062):
    /// интерфейс IF-015 различает только «живой/не живой» (FindLiveByHash),
    /// инспекция факта отзыва — по внутреннему словарю in-memory реализации
    /// (копия-снимок сущностей, контракт неизменен).
    /// </summary>
    public static RefreshToken? FindRefreshRecordByHash(B08WebAppFactory factory, string tokenHash)
    {
        var repository = factory.Services.GetRequiredService<ISecurityTokenRepository>();
        var field = typeof(InMemorySecurityTokenRepository).GetField(
            "_refreshTokens",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new Xunit.Sdk.XunitException(
                "Инспекция хранилища refresh-токенов: поле _refreshTokens не найдено "
                + "в InMemorySecurityTokenRepository.");
        var refreshTokens = (Dictionary<Guid, RefreshToken>)field.GetValue(repository)!;
        return refreshTokens.Values.FirstOrDefault(token => token.TokenHash == tokenHash);
    }

    /// <summary>
    /// Число записей кодов восстановления в хранилище тестового хоста (только
    /// чтение, для then «новых записей RecoveryCode не создано» кейса TS-070):
    /// интерфейс IF-015 перечисления не даёт, инспекция — по внутреннему словарю
    /// in-memory реализации (копия-снимок сущностей, контракт неизменен).
    /// </summary>
    public static int CountRecoveryCodeRecords(B08WebAppFactory factory)
    {
        var repository = factory.Services.GetRequiredService<ISecurityTokenRepository>();
        var field = typeof(InMemorySecurityTokenRepository).GetField(
            "_recoveryCodes",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new Xunit.Sdk.XunitException(
                "Инспекция хранилища кодов: поле _recoveryCodes не найдено "
                + "в InMemorySecurityTokenRepository.");
        var codes = (Dictionary<Guid, RecoveryCode>)field.GetValue(repository)!;
        return codes.Count;
    }

    /// <summary>Разбирает JSON-тело ответа; корень обязан быть объектом.</summary>
    public static async Task<JsonElement> ReadJsonObjectAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            return root.Clone();
        }
        catch (JsonException exception)
        {
            throw new Xunit.Sdk.XunitException($"Тело ответа не является JSON: «{content}» ({exception.Message}).");
        }
    }
}
