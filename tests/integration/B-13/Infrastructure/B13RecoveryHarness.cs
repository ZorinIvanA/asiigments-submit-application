using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>
/// Общие шаги recovery-кейсов батча B-13 (TS-162/TS-163): маршруты эндпойнтов
/// IF-008 и входа FR-007; given «живой код у email» — собственный
/// POST /auth/recovery/request, значение кода — из [DEV-EMAIL]-записи тестового
/// sink (IF-005); given «живой resetToken, значение известно тесту» — DI-минт
/// через ITokenService.CreatePasswordResetToken + ISecurityTokenRepository.Add
/// (методика ADR-010/ADR-022: харнес наполняет хранилища DI-сидом, given не
/// зависит от POST /auth/recovery/confirm); DI-сид пользователя с паролем —
/// хэш РЕАЛЬНЫМ IPasswordHasher хоста (IF-002), поэтому вход старым/новым
/// паролем (TS-163) сверяется с хранимым хэшем. Снимки Δkdf — тестовый шов
/// IKdfCounter.Snapshot() (IF-002/ADR-031, контракт заморожен); «счётчик KDF
/// сброшен» реализуется дельтами: снимок до и после измеряемого участка.
/// </summary>
public static class B13RecoveryHarness
{
    /// <summary>POST /auth/recovery/request (IF-008; предусловие «живой код»).</summary>
    public const string RecoveryRequestEndpoint = "/api/v1/auth/recovery/request";

    /// <summary>POST /auth/recovery/confirm (IF-008, FR-013).</summary>
    public const string RecoveryConfirmEndpoint = "/api/v1/auth/recovery/confirm";

    /// <summary>POST /auth/reset-password (IF-008, FR-014).</summary>
    public const string ResetPasswordEndpoint = "/api/v1/auth/reset-password";

    /// <summary>POST /auth/login (FR-007; ветки входа TS-163).</summary>
    public const string LoginEndpoint = "/api/v1/auth/login";

    /// <summary>Пароль DI-сид-пользователя до сброса (правила FR-006 соблюдены).</summary>
    public const string TestUserPassword = "Passw0rd!";

    /// <summary>Новый пароль кейса TS-163 (правила FR-006 соблюдены).</summary>
    public const string NewPassword = "NewPass1!";

    /// <summary>Клиент без авто-редиректов (точные статусы) и без cookie-контейнера.</summary>
    public static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    public static Task<HttpResponseMessage> RequestRecoveryCodeAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync(RecoveryRequestEndpoint, new { email });

    public static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string email, string code) =>
        client.PostAsJsonAsync(RecoveryConfirmEndpoint, new { email, code });

    public static Task<HttpResponseMessage> ResetPasswordAsync(
        HttpClient client,
        string resetToken,
        string password,
        string confirmPassword) =>
        client.PostAsJsonAsync(ResetPasswordEndpoint, new { resetToken, password, confirmPassword });

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginEndpoint, new { login, password });

    /// <summary>
    /// given «живой код C, значение известно тесту из записи 'EmailDev'»:
    /// POST /auth/recovery/request → 200 (предусловие; лимит recovery_request
    /// не исчерпан — свежий хост фикстуры, один запрос), затем извлечение
    /// 6-значного кода из [DEV-EMAIL]-записи тестового sink (IF-005).
    /// </summary>
    public static async Task<string> RequestLiveCodeAsync(
        B13RecoveryWebAppFactory factory,
        HttpClient client,
        string email)
    {
        using var response = await RequestRecoveryCodeAsync(client, email);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: POST /auth/recovery/request → 200, фактически " +
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return factory.LogSink.GetLastRecoveryCodeForEmail(email);
    }

    /// <summary>
    /// given «пользователь с известным паролем создан прямым DI-сидом в
    /// IUserRepository тестового хоста»: PasswordHash — реальный PBKDF2-хэш
    /// (IPasswordHasher.Hash хоста, метка seed, IF-002), поэтому вход старым
    /// паролем до сброса и новым после сверяются с хранимым хэшем. Снимки Δkdf
    /// кейсы снимают ПОСЛЕ сида. Повторный вызов с тем же login возвращает
    /// существующую запись (идемпотентность внутри класса-фикстуры).
    /// </summary>
    public static User SeedStudentWithPassword(
        WebApplicationFactory<Program> factory,
        string login,
        string fullName,
        string email,
        string password)
    {
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var existing = users.GetByLogin(login);
        if (existing is not null)
        {
            return existing;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = factory.Services.GetRequiredService<IPasswordHasher>()
                .Hash(password, KdfCallers.Seed),
            FullName = fullName,
            Role = UserRoles.Student,
            GroupId = null,
            CreatedAt = DateTime.UtcNow,
        };
        users.Add(user);
        return user;
    }

    /// <summary>
    /// given «живой resetToken T, значение известно тесту» (DI-минт, ADR-010):
    /// ITokenService.CreatePasswordResetToken (base64url(32B CSPRNG) + SHA-256-хэш
    /// + TTL 15 минут по TimeProvider, IF-003) и запись в
    /// ISecurityTokenRepository.Add (хранится только TokenHash, IF-015).
    /// Возвращает ОТКРЫТОЕ значение токена T.
    /// </summary>
    public static string SeedLiveResetToken(WebApplicationFactory<Program> factory, Guid userId)
    {
        var grant = factory.Services.GetRequiredService<ITokenService>()
            .CreatePasswordResetToken(userId);
        factory.Services.GetRequiredService<ISecurityTokenRepository>()
            .Add(new PasswordResetToken
            {
                TokenHash = grant.TokenHash,
                UserId = userId,
                ExpiresAt = grant.ExpiresAt,
                UsedAt = null,
            });
        return grant.Value;
    }

    /// <summary>Снимок счётчика KDF «метка вызывателя → число дериваций» (IF-002/ADR-031).</summary>
    public static IReadOnlyDictionary<string, long> KdfSnapshot(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IKdfCounter>().Snapshot();

    /// <summary>Δkdf по метке вызывателя (например reset_password) между снимками.</summary>
    public static long KdfCallerDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after,
        string caller)
    {
        var afterValue = after.TryGetValue(caller, out var a) ? a : 0;
        var beforeValue = before.TryGetValue(caller, out var b) ? b : 0;
        return afterValue - beforeValue;
    }
}
