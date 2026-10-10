using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B15.Infrastructure;

/// <summary>
/// HTTP-обёртка и предусловия кейсов восстановления/сброса пароля батча B-15
/// (TS-083, TS-084, TS-085, TS-190, TS-207): эндпойнты IF-008 и дословные тексты
/// ошибок (FR-023). given «живой код у пользователя» исполняется СВОЙСТВЕННЫМ
/// потоком FR-012→FR-013: POST /auth/recovery/request → 200, значение кода —
/// из [DEV-EMAIL]-записи тестового sink (IF-005). Пользователи создаются прямым
/// DI-сидом в IUserRepository тестового хоста (методика NFR-001/ADR-015);
/// пароль сида хэшируется РЕАЛЬНЫМ IPasswordHasher хоста (IF-002) — кейсы,
/// сверяющие применённый пароль с хранимым хэшем (TS-084), работают с настоящим
/// PBKDF2-хэшем. Клиенты — без cookie-контейнера, носители сессий передаются
/// заголовком Cookie ЯВНО (урок CR-002: CookieContainer не возвращает
/// Secure-cookie для http).
/// </summary>
public static class B15RecoveryApi
{
    /// <summary>POST /auth/recovery/request (IF-008, FR-012; предусловие «живой код»).</summary>
    public const string RecoveryRequestEndpoint = "/api/v1/auth/recovery/request";

    /// <summary>POST /auth/recovery/confirm (IF-008, FR-013).</summary>
    public const string RecoveryConfirmEndpoint = "/api/v1/auth/recovery/confirm";

    /// <summary>POST /auth/reset-password (IF-008, FR-014).</summary>
    public const string ResetPasswordEndpoint = "/api/v1/auth/reset-password";

    /// <summary>GET /auth/me (TS-085: «access B продолжает работать — 200»).</summary>
    public const string MeEndpoint = "/api/v1/auth/me";

    /// <summary>POST /auth/refresh (TS-085: «refresh B валиден — 204»).</summary>
    public const string RefreshEndpoint = "/api/v1/auth/refresh";

    /// <summary>Текст 400 VALIDATION (FR-014/FR-023, дословно).</summary>
    public const string InvalidDataMessage = "Данные заполнены неверно";

    /// <summary>Текст 400 RESET_LINK_INVALID (FR-014/FR-023, дословно).</summary>
    public const string ResetLinkInvalidMessage = "Ссылка восстановления недействительна или истекла";

    /// <summary>Текст 400 CODE_REJECTED (FR-013/FR-023, дословно).</summary>
    public const string CodeRejectedMessage = "Код восстановления не подходит";

    /// <summary>Пароль DI-сид-пользователей кейсов восстановления (правила FR-006 соблюдены).</summary>
    public const string TestUserPassword = "Passw0rd!";

    /// <summary>Валидный новый пароль сброса (правила FR-006 соблюдены; кейс FR-014 AC).</summary>
    public const string NewPassword = "NewPass1!";

    /// <summary>
    /// Анонимный клиент хоста: без авто-редиректов (точные статусы) и без
    /// cookie-контейнера (носители сессий — явные заголовки, CR-002).
    /// </summary>
    public static HttpClient CreateClient(B15WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>given «пользователь role создан прямым DI-сидом с реальным хэшем пароля».</summary>
    public static User SeedUser(
        B15WebAppFactory factory,
        string login,
        string email,
        string fullName,
        string role)
    {
        var passwordHash = factory.Services
            .GetRequiredService<IPasswordHasher>()
            .Hash(TestUserPassword, KdfCallers.Seed);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = passwordHash,
            FullName = fullName,
            Role = role,
            GroupId = null,
            CreatedAt = DateTime.UtcNow,
        };

        factory.Services.GetRequiredService<IUserRepository>().Add(user);
        return user;
    }

    /// <summary>
    /// given «действующий refresh-токен сессии»: ITokenService.CreateRefreshToken +
    /// запись в IRefreshTokenRepository (хранится только TokenHash, IF-003/IF-015) —
    /// ровно состояние, которое создаёт выпуск refresh на устройстве.
    /// Возвращает ОТКРЫТОЕ значение токена для refresh-cookie.
    /// </summary>
    public static string SeedRefreshToken(B15WebAppFactory factory, Guid userId)
    {
        var grant = factory.Services
            .GetRequiredService<ITokenService>()
            .CreateRefreshToken(userId);

        factory.Services.GetRequiredService<IRefreshTokenRepository>().Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = grant.TokenHash,
            ExpiresAt = grant.ExpiresAt,
            CreatedAt = DateTime.UtcNow,
        });

        return grant.Value;
    }

    /// <summary>Шаг/предусловие «запрос кода»: POST /auth/recovery/request.</summary>
    public static Task<HttpResponseMessage> RequestRecoveryCodeAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync(RecoveryRequestEndpoint, new { email });

    /// <summary>Шаг «подтверждение кода»: POST /auth/recovery/confirm.</summary>
    public static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string email, string code) =>
        client.PostAsJsonAsync(RecoveryConfirmEndpoint, new { email, code });

    /// <summary>Шаг «сброс пароля»: POST /auth/reset-password.</summary>
    public static Task<HttpResponseMessage> ResetPasswordAsync(
        HttpClient client,
        string resetToken,
        string password,
        string confirmPassword) =>
        client.PostAsJsonAsync(ResetPasswordEndpoint, new { resetToken, password, confirmPassword });

    /// <summary>
    /// Шаг «POST /auth/refresh с refresh-cookie сессии»: явный заголовок Cookie
    /// refresh_token={значение} (имя — константа Auth.Core, IF-003/ADR-022).
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
