using LabsApp.Domain.Entities;
using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.Tests.Infrastructure;

/// <summary>
/// Параметры DI-сида пользователя для <see cref="TestSession"/>: незаданные поля
/// замещаются умолчаниями хелпера (уникальный логин генерируется от роли —
/// параллельные сессии одного хоста не конфликтуют по ci-уникальности, IF-015).
/// </summary>
public sealed record TestUserSeed
{
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

    internal string ResolveFullName() => FullName ?? DefaultFullName;
}

/// <summary>
/// Сквозной хелпер тестовых пользователей (ADR-015, T-006 — единый хелпер волны
/// контроллеров): пользователь сеется напрямую в IUserRepository из
/// factory.Services; клиент — анонимный (аутентификации нет). Сессия владеет
/// клиентом — Dispose освобождает его.
/// </summary>
public sealed class TestSession : IDisposable
{
    private TestSession(User user, HttpClient client)
    {
        User = user;
        Client = client;
    }

    /// <summary>Сидированная запись пользователя (копия-снимок из репозитория).</summary>
    public User User { get; }

    /// <summary>Клиент сценария (анонимный).</summary>
    public HttpClient Client { get; }

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

    /// <summary>Клиент сценария с DI-сидом студента.</summary>
    public static Task<TestSession> CreateStudent(WebApplicationFactory<Program> factory, TestUserSeed? seed = null) =>
        Create(factory, UserRoles.Student, seed);

    /// <summary>Клиент сценария с DI-сидом преподавателя.</summary>
    public static Task<TestSession> CreateTeacher(WebApplicationFactory<Program> factory, TestUserSeed? seed = null)
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

    /// <summary>DI-сид студента без создания клиента.</summary>
    public static Task<User> SeedStudent(WebApplicationFactory<Program> factory, TestUserSeed? seed = null) =>
        Seed(factory, UserRoles.Student, seed);

    /// <summary>DI-сид преподавателя без создания клиента.</summary>
    public static Task<User> SeedTeacher(WebApplicationFactory<Program> factory, TestUserSeed? seed = null)
    {
        if (seed?.GroupName is not null)
        {
            throw new ArgumentException(
                "У teacher группа всегда null (домен); GroupName задаётся только студенту.",
                nameof(seed));
        }

        return Seed(factory, UserRoles.Teacher, seed);
    }

    private static async Task<TestSession> Create(WebApplicationFactory<Program> factory, string role, TestUserSeed? seed)
    {
        var user = await Seed(factory, role, seed);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        return new TestSession(user, client);
    }

    private static async Task<User> Seed(WebApplicationFactory<Program> factory, string role, TestUserSeed? seed)
    {
        ArgumentNullException.ThrowIfNull(factory);
        seed ??= new TestUserSeed();

        var services = factory.Services;
        var users = services.GetRequiredService<IUserRepository>();
        var groups = services.GetRequiredService<IGroupRepository>();
        var timeProvider = services.GetRequiredService<TimeProvider>();

        var login = seed.Login ?? $"{role}-{Guid.NewGuid().ToString("N")[..8]}";
        var groupId = seed.GroupName is { } groupName
            ? await ResolveGroupId(groups, groupName, timeProvider)
            : (Guid?)null;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = seed.Email ?? $"{login}@example.com",
            PasswordHash = "!",
            FullName = seed.ResolveFullName(),
            Role = role,
            GroupId = groupId,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        await users.AddAsync(user);

        // Копия-снимок из репозитория (мутации — только через IUserRepository.Update).
        return (await users.GetByLoginAsync(login))
            ?? throw new InvalidOperationException($"Пользователь {login} не сохранился при DI-сиде.");
    }

    /// <summary>Группа по имени (ci-правило) либо созданная; Id для GroupId студента.</summary>
    private static async Task<Guid> ResolveGroupId(IGroupRepository groups, string groupName, TimeProvider timeProvider)
    {
        if (await groups.GetByNameAsync(groupName) is { } existing)
        {
            return existing.Id;
        }

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = groupName,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        await groups.AddAsync(group);
        return group.Id;
    }
}
