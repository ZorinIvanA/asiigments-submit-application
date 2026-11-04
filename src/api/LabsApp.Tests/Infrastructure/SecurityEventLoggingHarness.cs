using System.Net;
using System.Net.Http.Json;
using System.Text;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Infrastructure;
using LabsApp.Observability;
using LabsApp.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabsApp.Tests.Infrastructure;

/// <summary>
/// Хелпер recovery-сценариев с инспекцией фактов безопасности: DI-сид
/// пользователей/refresh-токенов, cookie-клиенты и записи «Api.Security»
/// из log-sink (перенесён из удалённых observability-тестов; используется
/// recovery-тестами как шов проверки журнала безопасности).
/// </summary>
internal static class SecurityEventLoggingHarness
{
    public const string PasswordEndpoint = "/api/v1/me/password";
    public const string LogoutEndpoint = "/api/v1/auth/logout";
    public const string LoginEndpoint = "/api/v1/auth/login";

    /// <summary>Пароль DI-сид-пользователей до смены (правила FR-006 соблюдены).</summary>
    public const string TestUserPassword = "student123!";

    /// <summary>Валидный новый пароль смены.</summary>
    public const string NewPassword = "NewPass1!";

    public static HttpClient CreateAnonymousClient(TestWebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>DI-сид пользователя с РЕАЛЬНЫМ PBKDF2-хэшем (метка seed).</summary>
    public static async Task<User> SeedUser(TestWebAppFactory factory, string login, string password = TestUserPassword)
    {
        var passwordHash = factory.Services
            .GetRequiredService<IPasswordHasher>()
            .Hash(password, KdfCallers.Seed);
        var repository = factory.Services.GetRequiredService<IUserRepository>();
        await repository.AddAsync(new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = $"{login}@example.com",
            PasswordHash = passwordHash,
            FullName = "Тест Тестович Тестов",
            Role = UserRoles.Student,
            GroupId = null,
            CreatedAt = DateTime.UtcNow,
        });
        return (await repository.GetByLoginAsync(login))
            ?? throw new InvalidOperationException($"Пользователь {login} не сохранился при DI-сиде.");
    }

    /// <summary>
    /// given «живой refresh-токен»: ITokenService.CreateRefreshToken + запись в
    /// ISecurityTokenRepository; возвращает ОТКРЫТОЕ значение (для cookie/маркера).
    /// </summary>
    public static async Task<string> SeedRefreshToken(TestWebAppFactory factory, Guid userId)
    {
        var grant = factory.Services
            .GetRequiredService<ITokenService>()
            .CreateRefreshToken(userId);

        await factory.Services.GetRequiredService<ISecurityTokenRepository>().AddAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = grant.TokenHash,
            ExpiresAt = grant.ExpiresAt,
            CreatedAt = DateTime.UtcNow,
        });

        return grant.Value;
    }

    /// <summary>Клиент с ЯВНЫМ заголовком Cookie (access и/или refresh по переданным значениям).</summary>
    public static HttpClient CreateCookieClient(TestWebAppFactory factory, string cookie)
    {
        var client = CreateAnonymousClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    public static string AccessCookieOf(TestWebAppFactory factory, Guid userId, string role = UserRoles.Student) =>
        $"{AuthCoreDefaults.AccessTokenCookieName}={factory.Services.GetRequiredService<ITokenService>().IssueAccessToken(userId, role)}";

    /// <summary>Значения заголовков Set-Cookie ответа (пустой массив — заголовка нет).</summary>
    public static string[] SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];

    /// <summary>
    /// Значение access-JWT из Set-Cookie (пара «имя=значение» до первого «;») —
    /// для разбора клеймов выпущенного токена (AC «Минт login-claim»).
    /// </summary>
    public static string AccessJwtFromSetCookie(HttpResponseMessage response)
    {
        var header = Assert.Single(SetCookies(response), cookie =>
            cookie.StartsWith($"{AuthCoreDefaults.AccessTokenCookieName}=", StringComparison.Ordinal));
        return header[(header.IndexOf('=') + 1)..].Split(';')[0];
    }

    public static Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string current, string password) =>
        client.PutAsync(
            PasswordEndpoint,
            new StringContent(
                $$"""{"currentPassword":"{{current}}","password":"{{password}}","confirmPassword":"{{password}}"}""",
                Encoding.UTF8,
                "application/json"));

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginEndpoint, new { login, password });

    /// <summary>Записи «Api.Security» текущего sink-снимка.</summary>
    public static IReadOnlyList<TestLogRecord> SecurityRecords(TestWebAppFactory factory) =>
        [.. factory.LogSink.Snapshot().Where(record => record.Category == SecurityEventLogger.LogCategory)];

    /// <summary>Сериализованная запись (сообщение + шаблон + состояние) — поверхность маркерной проверки NFR-006.</summary>
    public static string SerializeForMarkerCheck(TestLogRecord record) =>
        string.Join(
            "|",
            new[]
            {
                record.Category,
                record.Level.ToString(),
                record.Message,
                record.MessageTemplate ?? string.Empty,
            }.Concat(record.State.Select(pair => $"{pair.Key}={pair.Value}")));
}
