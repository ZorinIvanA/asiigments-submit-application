using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.Tests.Hosting;

/// <summary>
/// Параметры DI-сида пользователя для <see cref="TestSession"/>: незаданные поля
/// замещаются умолчаниями хелпера (уникальный логин генерируется от роли —
/// параллельные сессии одного хоста не конфликтуют по ci-уникальности, IF-015).
/// </summary>
public sealed record TestUserSeed
{
    /// <summary>Пароль DI-сидируемых пользователей по умолчанию (правила пароля FR-006 соблюдены).</summary>
    public const string DefaultPassword = "Passw0rd!";

    private const string DefaultFullName = "Тест Тестович Тестов";

    /// <summary>Логин; null — уникальный «{роль}-{8 hex}».</summary>
    public string? Login { get; init; }

    /// <summary>Email; null — «{логин}@example.com».</summary>
    public string? Email { get; init; }

    /// <summary>ФИО; null — «Тест Тестович Тестов».</summary>
    public string? FullName { get; init; }

    /// <summary>
    /// Имя группы студента: группа создаётся при отсутствии (ci-поиск, IF-015) и
    /// проставляется в GroupId. Для teacher не допускается (домен: у teacher группа
    /// всегда null) — ArgumentException на этапе сида.
    /// </summary>
    public string? GroupName { get; init; }

    /// <summary>Пароль учётки (хэшируется хэшером хоста, метка seed — ADR-007).</summary>
    public string Password { get; init; } = DefaultPassword;

    internal string ResolveFullName() => FullName ?? DefaultFullName;
}

/// <summary>
/// Сквозная тестовая сессия (ADR-015, T-006 — единый хелпер волны контроллеров):
/// пользователь сеется напрямую в IUserRepository из factory.Services (пароль —
/// настоящий IPasswordHasher.Hash, метка seed), access-JWT минтится
/// ITokenService.IssueAccessToken (реальный компонент C-004) и предустанавливается
/// заголовком Cookie клиента и в контейнере <see cref="Cookies"/> по имени-константе
/// AuthCoreDefaults. POST /auth/login для создания сессии НЕ используется (ADR-015:
/// волна доменных задач исполняется параллельно с задачей login-эндпойнта).
/// Клиент — без авто-редиректов (точные статусы) и без автоматического
/// cookie-контейнера: сценарий переносит cookie сам через
/// <see cref="TestCookieContainer.CaptureFrom"/>/<see cref="TestCookieContainer.ApplyTo"/>.
/// Сессия владеет клиентом — Dispose освобождает его.
/// </summary>
public sealed class TestSession : IDisposable
{
    private TestSession(User user, HttpClient client, TestCookieContainer cookies)
    {
        User = user;
        Client = client;
        Cookies = cookies;
    }

    /// <summary>Сидированная запись пользователя (копия-снимок из репозитория).</summary>
    public User User { get; }

    /// <summary>Клиент с предустановленной access-cookie сессии.</summary>
    public HttpClient Client { get; }

    /// <summary>
    /// Cookie-контейнер сессии: предзаполнен access-cookie; сценарии auth-потоков
    /// (refresh/logout) дополняют его через CaptureFrom ответов.
    /// </summary>
    public TestCookieContainer Cookies { get; }

    /// <summary>
    /// Фиксирует RemoteIpAddress соединения для всех запросов клиента (IP-шов,
    /// <see cref="TestWebAppFactory.SetClientIp"/>); null — снять подмену.
    /// </summary>
    public void SetClientIp(string? ipAddress) => TestWebAppFactory.SetClientIp(Client, ipAddress);

    /// <inheritdoc/>
    public void Dispose() => Client.Dispose();

    // ------------------------------------------------------------------
    // Фабрики сессий (ADR-015: CreateStudent/CreateTeacher)
    // ------------------------------------------------------------------

    /// <summary>Сессия студента: DI-сид + минт access-cookie (без POST /auth/login).</summary>
    public static TestSession CreateStudent(WebApplicationFactory<Program> factory, TestUserSeed? seed = null) =>
        Create(factory, UserRoles.Student, seed);

    /// <summary>Сессия преподавателя: DI-сид + минт access-cookie (без POST /auth/login).</summary>
    public static TestSession CreateTeacher(WebApplicationFactory<Program> factory, TestUserSeed? seed = null)
    {
        if (seed?.GroupName is not null)
        {
            throw new ArgumentException(
                "У teacher группа всегда null (домен); GroupName задаётся только студенту.",
                nameof(seed));
        }

        return Create(factory, UserRoles.Teacher, seed);
    }

    // ------------------------------------------------------------------
    // DI-сид без сессии (given «существующий пользователь» login-веток и т.п.)
    // ------------------------------------------------------------------

    /// <summary>DI-сид студента без создания клиента (пароль — настоящий хэш хоста).</summary>
    public static User SeedStudent(WebApplicationFactory<Program> factory, TestUserSeed? seed = null) =>
        Seed(factory, UserRoles.Student, seed);

    /// <summary>DI-сид преподавателя без создания клиента (пароль — настоящий хэш хоста).</summary>
    public static User SeedTeacher(WebApplicationFactory<Program> factory, TestUserSeed? seed = null)
    {
        if (seed?.GroupName is not null)
        {
            throw new ArgumentException(
                "У teacher группа всегда null (домен); GroupName задаётся только студенту.",
                nameof(seed));
        }

        return Seed(factory, UserRoles.Teacher, seed);
    }

    private static TestSession Create(WebApplicationFactory<Program> factory, string role, TestUserSeed? seed)
    {
        var user = Seed(factory, role, seed);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        var cookies = new TestCookieContainer();
        MintAccessCookie(factory, client, cookies, user);

        return new TestSession(user, client, cookies);
    }

    /// <summary>
    /// Минт access-cookie (ADR-015): ITokenService.IssueAccessToken(userId, role,
    /// login) — прод-форма с login (канал subject, аменда CR-001/ADR-044);
    /// значение кладётся в заголовок Cookie клиента и в контейнер по имени
    /// AuthCoreDefaults.AccessTokenCookieName (единый источник с ICookieService).
    /// </summary>
    private static void MintAccessCookie(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        TestCookieContainer cookies,
        User user)
    {
        var jwt = factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(user.Id, user.Role, user.Login);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={jwt}");
        cookies.Set(AuthCoreDefaults.AccessTokenCookieName, jwt);
    }

    private static User Seed(WebApplicationFactory<Program> factory, string role, TestUserSeed? seed)
    {
        ArgumentNullException.ThrowIfNull(factory);
        seed ??= new TestUserSeed();

        var services = factory.Services;
        var users = services.GetRequiredService<IUserRepository>();
        var groups = services.GetRequiredService<IGroupRepository>();
        var hasher = services.GetRequiredService<IPasswordHasher>();
        var timeProvider = services.GetRequiredService<TimeProvider>();

        var login = seed.Login ?? $"{role}-{Guid.NewGuid().ToString("N")[..8]}";
        var groupId = seed.GroupName is { } groupName
            ? ResolveGroupId(groups, groupName, timeProvider)
            : (Guid?)null;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = seed.Email ?? $"{login}@example.com",
            // Метка 'seed' (ADR-007): DI-сид тестовых учёток — та же статья KDF,
            // что и сид-хэши SeedRunner.
            PasswordHash = hasher.Hash(seed.Password, KdfCallers.Seed),
            FullName = seed.ResolveFullName(),
            Role = role,
            GroupId = groupId,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        users.Add(user);

        // Копия-снимок из репозитория (мутации — только через IUserRepository.Update).
        return users.GetByLogin(login)
            ?? throw new InvalidOperationException($"Пользователь {login} не сохранился при DI-сиде.");
    }

    /// <summary>Группа по имени (ci-правило) либо созданная; Id для GroupId студента.</summary>
    private static Guid ResolveGroupId(IGroupRepository groups, string groupName, TimeProvider timeProvider)
    {
        if (groups.GetByName(groupName) is { } existing)
        {
            return existing.Id;
        }

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = groupName,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        groups.Add(group);
        return group.Id;
    }
}
