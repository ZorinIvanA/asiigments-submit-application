using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Хелпер сессий батча B-06 (ADR-010/ADR-022, механика TestSession T-006): пользователь
/// сеется напрямую в IUserRepository из factory.Services (пароль — настоящий
/// IPasswordHasher.Hash, IF-002), access-cookie минтится ITokenService.IssueAccessToken
/// (реальный компонент C-004) и кладётся в cookie-контейнер клиента по константам
/// Auth.Core (имя/Path — единый источник с ICookieService) — БЕЗ зависимости от
/// POST /auth/login. Используется кейсами «Действующая сессия student» (TS-027/TS-028)
/// и «uuid существующего пользователя» (TS-032).
/// </summary>
public static class TestSessions
{
    /// <summary>Пароль всех DI-сидируемых пользователей (правила §8 соблюдены).</summary>
    public const string TestUserPassword = "Passw0rd!";

    /// <summary>
    /// DI-сид пользователя: добавляет запись в IUserRepository (ci-уникальность под
    /// общим StorageLock, IF-015) и возвращает копию-снимок.
    /// </summary>
    public static User SeedUser(
        WebApplicationFactory<Program> factory,
        string login,
        string email,
        string fullName = "Тестовый Студент",
        string role = UserRoles.Student)
    {
        var services = factory.Services;
        var users = services.GetRequiredService<IUserRepository>();
        var hasher = services.GetRequiredService<IPasswordHasher>();
        var timeProvider = services.GetRequiredService<TimeProvider>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            // Метка 'seed' (ADR-007): DI-сид тестовых учёток — та же статья KDF,
            // что и сид-хэши SeedRunner; кейс сам пароль не проверяет напрямую.
            PasswordHash = hasher.Hash(TestUserPassword, KdfCallers.Seed),
            FullName = fullName,
            Role = role,
            GroupId = null,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        users.Add(user);
        return user;
    }

    /// <summary>Клиент с минтованной access-cookie действующей сессии (роль — из записи).</summary>
    public static HttpClient CreateSessionClient(WebApplicationFactory<Program> factory, Guid userId, string role)
    {
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        var accessToken = tokens.IssueAccessToken(userId, role);
        return HostClients.CreateWithCookie(factory, AuthCoreDefaults.AccessTokenCookieName, accessToken);
    }

    /// <summary>
    /// given «Действующая сессия student»: DI-сид студента + минт access-cookie.
    /// Возвращает клиента с сессией и сидированную запись (uuid пользователя).
    /// </summary>
    public static (HttpClient Client, User Seeded) CreateStudentSession(
        WebApplicationFactory<Program> factory,
        string login,
        string email,
        string fullName = "Тестовый Студент")
    {
        var user = SeedUser(factory, login, email, fullName, UserRoles.Student);
        return (CreateSessionClient(factory, user.Id, user.Role), user);
    }
}
