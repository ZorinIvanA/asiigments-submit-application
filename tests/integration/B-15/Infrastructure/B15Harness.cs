using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B15.Infrastructure;

/// <summary>
/// Предусловия и сессии тестового хоста батча B-15 — по переизданным формулировкам
/// кейсов (арбитраж a-017/CR-001):
///  - пользователи и группы создаются ПРЯМЫМ DI-сидом в IUserRepository/IGroupRepository
///    тестового хоста (методика NFR-001/FR-007/ADR-010); POST /auth/register и
///    POST /auth/login в предусловиях НЕ вызываются;
///  - сессия — cookie access_token с JWT HS256, минтым ХАРНЕСОМ ключом Auth__JwtKey
///    тестового хоста: выпуск через ITokenService хоста (методика ADR-022) даёт
///    claims sub={uuid}, role='student', iat=now, exp=iat+Auth__AccessTtlMinutes,
///    подпись HS256 ключом хоста; jti — уникальный НЕПРОЗРАЧНЫЙ идентификатор по
///    IF-003, ITokenService генерирует его самостоятельно (Guid.NewGuid()) и он НЕ
///    равен uuid пользователя — не опираться на jti=uuid (реворк-правка CR-002
///    комментария; семантику given кейсов несут claims sub/role/iat/exp);
///  - cookie передаётся заголовком Cookie ЯВНО на каждый запрос клиента (урок CR-002:
///    CookieContainer не возвращает Secure-cookie для не-https URI — в Production-кейсе
///    TS-095 это был бы ложный 401).
/// Информация о пользователях для проверок хранилища — IUserRepository из
/// factory.Services (чтение возвращает копии-снимки; контракт IF-015).
/// </summary>
public static class B15Harness
{
    public const string ProfileEndpoint = "/api/v1/me/profile";

    /// <summary>Перечень семестров (FR-018): GET /api/v1/semesters, любая авторизованная роль.</summary>
    public const string SemestersEndpoint = "/api/v1/semesters";

    /// <summary>
    /// Маркер-заглушка passwordHash DI-сид-пользователей: кейсы батча не используют
    /// пароль (логин/смена пароля в предусловиях отсутствуют), хранилище поле не валидирует.
    /// </summary>
    public const string SeededPasswordHashMark = "b15-di-seed-no-login";

    /// <summary>
    /// given «сессия пользователя»: клиент без cookie-контейнера с ЯВНЫМ заголовком
    /// Cookie access_token={минтированный access-JWT HS256 ключом Auth__JwtKey хоста}.
    /// Роль сессии — student (кейсы профиля и semesters под студентом).
    /// </summary>
    public static HttpClient CreateSessionClient(B15WebAppFactory factory, Guid userId) =>
        CreateSessionClient(factory, userId, UserRoles.Student);

    /// <summary>
    /// given «сессия роли role»: клиент без cookie-контейнера с ЯВНЫМ заголовком
    /// Cookie access_token={минтированный access-JWT HS256 ключом Auth__JwtKey хоста,
    /// role=role} — сессия преподавателя (кейсы semesters, TS-121) минтится тем же
    /// способом (ADR-015/CR-001), без POST /auth/login.
    /// </summary>
    public static HttpClient CreateSessionClient(B15WebAppFactory factory, Guid userId, string role)
    {
        // Минт харнесом: HS256, sub/jti/iat + role, exp = iat + Auth__AccessTtlMinutes
        // (ITokenService, IF-003); ключ — AuthOptions хоста, заданный фабрикой.
        var token = factory.Services
            .GetRequiredService<ITokenService>()
            .IssueAccessToken(userId, role);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        // Имя cookie — константа Auth.Core (единый источник с ICookieService, ADR-022).
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={token}");
        return client;
    }

    /// <summary>
    /// given «пользователь role=student с текущими fullName/email создан прямым
    /// DI-сидом в IUserRepository тестового хоста; uuid известен харнесу».
    /// </summary>
    public static User SeedStudent(
        B15WebAppFactory factory,
        string fullName,
        string login,
        string email,
        Guid? groupId = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = SeededPasswordHashMark,
            FullName = fullName,
            Role = UserRoles.Student,
            GroupId = groupId,
            CreatedAt = DateTime.UtcNow,
        };

        factory.Services.GetRequiredService<IUserRepository>().Add(user);
        return user;
    }

    /// <summary>given «группа создана DI-сидом IGroupRepository» (кейс TS-096).</summary>
    public static Group SeedGroup(B15WebAppFactory factory, string name)
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
    /// given «лабораторная работа создана DI-сидом ILabRepository» (кейсы semesters
    /// TS-119/TS-121): запись Lab добавляется напрямую в ILabRepository тестового
    /// хоста — семестр/номер передаёт вызывающий, остальное — нейтральные значения.
    /// </summary>
    public static Lab SeedLab(B15WebAppFactory factory, int semester, int number)
    {
        var now = DateTime.UtcNow;
        var lab = new Lab
        {
            Id = Guid.NewGuid(),
            Semester = semester,
            Number = number,
            Content = $"Лабораторная {semester}:{number} (сид B-15)",
            AssignmentUrl = null,
            DefenseRequired = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        factory.Services.GetRequiredService<ILabRepository>().Add(lab);
        return lab;
    }

    /// <summary>Пользователь по uuid из хранилища тестового хоста (копия-снимок, IF-015).</summary>
    public static User UserById(B15WebAppFactory factory, Guid id)
    {
        var user = factory.Services.GetRequiredService<IUserRepository>().GetById(id);
        Assert.NotNull(user);
        return user;
    }

    /// <summary>Пользователь по email (ci) из хранилища тестового хоста (копия-снимок, IF-015).</summary>
    public static User UserByEmail(B15WebAppFactory factory, string email)
    {
        var user = factory.Services.GetRequiredService<IUserRepository>().GetByEmail(email);
        Assert.NotNull(user);
        return user;
    }
}
