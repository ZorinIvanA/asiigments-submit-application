using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using LabsApp.Tests.Hosting;
using LabsApp.Tests.Students;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LabsApp.Tests.Profile;

// ============================================================================
// Эндпойнт-тесты аменды CR-001 (T-204, ADR-042): мутации PUT /me/password и
// PUT /me/profile обязаны быть УЗКИМИ атомарными операциями репозитория —
// IUserRepository.SetPassword (только PasswordHash) и IUserRepository.UpdateProfile
// (только FullName+Email с перебинтовкой byEmail). Полная замена записи
// устаревшим снимком откатывала бы конкурентные мутации соседних полей
// (суть CR-001: смена пароля ↔ правка профиля ↔ назначение группы).
//
// Окно гонки детерминировано швом-декоратором IUserRepository
// (<see cref="GatedUserRepository"/> из StudentsSetGroupAtomicityEndpointTests):
// первый GetById отслеживаемого пользователя — это TryLoadCurrentUser
// контроллера (CookieAuthenticationHandler хранилище не читает) — сигналит
// Arrived и удерживает контроллер, пока тест выполняет конкурентные мутации
// НАПРЯМУЮ через репозиторий, затем Release продолжает запрос.
// ============================================================================

/// <summary>
/// Фикстура хоста с декоратором-швом IUserRepository (демо-сид выключен, тестовые
/// итерации KDF 1000 — Δkdf-гейты считаются снимками IKdfCounter, FR-027).
/// </summary>
public sealed class Cr001GateFixture : IDisposable
{
    private readonly TestWebAppFactory _root = new(
        null,
        new Dictionary<string, string?>
        {
            [SeedOptions.DemoDataVariable] = "false",
            [AuthOptions.Pbkdf2IterationsVariable] = "1000",
        });

    /// <summary>Хост с подменённым IUserRepository (запросы и DI-сид — только он).</summary>
    public WebApplicationFactory<Program> Host { get; }

    /// <summary>Шов детерминированной гонки (один на хост, Arm на каждый сценарий).</summary>
    public GatedUserRepository Gate { get; }

    public Cr001GateFixture()
    {
        Host = _root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<GatedUserRepository>();
                services.AddSingleton<IUserRepository>(static sp =>
                    sp.GetRequiredService<GatedUserRepository>());
            });
        });
        Gate = Host.Services.GetRequiredService<GatedUserRepository>();
    }

    public void Dispose()
    {
        Host.Dispose();
        _root.Dispose();
    }
}

/// <summary>
/// Харнес тестов CR-001 над хостом с швом (WebApplicationFactory&lt;Program&gt;):
/// DI-сид пользователей с реальным PBKDF2-хэшем и токенов, снимки KDF-счётчика,
/// чтение JSON-тел и конверта ошибок IF-001.
/// </summary>
internal static class Cr001GateHarness
{
    public const string PasswordEndpoint = "/api/v1/me/password";
    public const string ProfileEndpoint = "/api/v1/me/profile";
    public const string ResetEndpoint = "/api/v1/auth/reset-password";

    /// <summary>Пароль DI-сид-пользователей до мутации (правила FR-006 соблюдены).</summary>
    public const string TestUserPassword = "student123!";

    /// <summary>Валидный новый пароль (буквально из when кейсов FR-014/FR-016).</summary>
    public const string NewPassword = "NewPass1!";

    public static HttpClient CreateAnonymousClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// Клиент с ЯВНЫМ заголовком Cookie access_token={минтованный access-JWT};
    /// <paramref name="refreshTokenValue"/> — значение refresh-cookie текущей
    /// сессии (кейс ISS-002).
    /// </summary>
    public static HttpClient CreateSessionClient(
        WebApplicationFactory<Program> factory,
        Guid userId,
        string role,
        string? refreshTokenValue = null)
    {
        var jwt = factory.Services
            .GetRequiredService<ITokenService>()
            .IssueAccessToken(userId, role);

        var cookie = $"{AuthCoreDefaults.AccessTokenCookieName}={jwt}";
        if (refreshTokenValue is not null)
        {
            cookie += $"; {AuthCoreDefaults.RefreshTokenCookieName}={refreshTokenValue}";
        }

        var client = CreateAnonymousClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    /// <summary>
    /// DI-сид пользователя с РЕАЛЬНЫМ PBKDF2-хэшем пароля (метка seed) и
    /// опциональной группой одной вставкой.
    /// </summary>
    public static User SeedHashedUser(
        WebApplicationFactory<Program> factory,
        string login,
        string role = UserRoles.Student,
        Guid? groupId = null)
    {
        var passwordHash = factory.Services
            .GetRequiredService<IPasswordHasher>()
            .Hash(TestUserPassword, KdfCallers.Seed);
        var repository = factory.Services.GetRequiredService<IUserRepository>();
        repository.Add(new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = $"{login}@example.com",
            PasswordHash = passwordHash,
            FullName = "Тест Тестович Тестов",
            Role = role,
            GroupId = groupId,
            CreatedAt = DateTime.UtcNow,
        });
        return repository.GetByLogin(login)
            ?? throw new InvalidOperationException($"Пользователь {login} не сохранился при DI-сиде.");
    }

    public static Group SeedGroup(WebApplicationFactory<Program> factory, string name)
    {
        var repository = factory.Services.GetRequiredService<IGroupRepository>();
        repository.Add(new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTime.UtcNow,
        });
        return repository.GetByName(name)
            ?? throw new InvalidOperationException($"Группа {name} не сохранилась при DI-сиде.");
    }

    public static IUserRepository Users(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IUserRepository>();

    /// <summary>
    /// given «действующий refresh-токен устройства»: ITokenService.CreateRefreshToken
    /// + запись в ISecurityTokenRepository; возвращает ОТКРЫТОЕ значение токена.
    /// </summary>
    public static string SeedRefreshToken(WebApplicationFactory<Program> factory, Guid userId)
    {
        var grant = factory.Services
            .GetRequiredService<ITokenService>()
            .CreateRefreshToken(userId);

        factory.Services.GetRequiredService<ISecurityTokenRepository>().Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = grant.TokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
        });

        return grant.Value;
    }

    /// <summary>
    /// given «живой reset-токен» (DI-сид): возвращает ОТКРЫТОЕ значение, в
    /// хранилище — только SHA-256-дайджест (IF-003).
    /// </summary>
    public static string SeedResetToken(WebApplicationFactory<Program> factory, Guid userId)
    {
        var grant = factory.Services
            .GetRequiredService<ITokenService>()
            .CreatePasswordResetToken(userId);

        factory.Services.GetRequiredService<ISecurityTokenRepository>().Add(new PasswordResetToken
        {
            TokenHash = grant.TokenHash,
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            UsedAt = null,
        });

        return grant.Value;
    }

    /// <summary>Пользователь по uuid из хранилища тестового хоста (копия-снимок).</summary>
    public static User StoredUser(WebApplicationFactory<Program> factory, Guid id) =>
        factory.Services.GetRequiredService<IUserRepository>().GetById(id)
        ?? throw new InvalidOperationException("Пользователь исчез из хранилища.");

    /// <summary>
    /// ЖИВАЯ запись refresh-токена по ОТКРЫТОМУ значению либо null (live-only
    /// FindLiveByHash, IF-015): отозванная запись из живой выборки исчезает.
    /// </summary>
    public static RefreshToken? RefreshTokenRecord(
        WebApplicationFactory<Program> factory, string tokenValue) =>
        factory.Services.GetRequiredService<ISecurityTokenRepository>()
            .FindLiveByHash(Sha256Hex(tokenValue));

    /// <summary>
    /// ЖИВАЯ запись reset-токена по ОТКРЫТОМУ значению либо null (live-only
    /// FindLiveResetByHash, IF-015): погашенный токен из выборки исчезает.
    /// </summary>
    public static PasswordResetToken? ResetTokenRecord(
        WebApplicationFactory<Program> factory, string tokenValue) =>
        factory.Services.GetRequiredService<ISecurityTokenRepository>()
            .FindLiveResetByHash(Sha256Hex(tokenValue));

    /// <summary>Снимок KDF-счётчика (тестовый шов IKdfCounter, FR-027/ADR-031).</summary>
    public static IReadOnlyDictionary<string, long> KdfSnapshot(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IKdfCounter>().Snapshot();

    /// <summary>Δkdf суммарно по всем меткам между снимками.</summary>
    public static long KdfDelta(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after) =>
        after.Values.Sum() - before.Values.Sum();

    /// <summary>Δkdf по ОДНОЙ метке вызывателя (гейты Δkdf(change_password/reset_password), FR-027).</summary>
    public static long KdfDeltaOfCaller(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after,
        string caller) =>
        after.GetValueOrDefault(caller) - before.GetValueOrDefault(caller);

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    /// <summary>Верхнеуровневый message конверта ошибок (IF-001).</summary>
    public static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Object, body.RootElement.ValueKind);
        return body.RootElement.GetProperty("message").GetString() ?? string.Empty;
    }

    /// <summary>SHA-256 hex (строчные) значения токена — зеркало TokenService (IF-003).</summary>
    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>
/// AC T-204 «Смена пароля не откатывает группу (суть CR-001)»: конкурентный
/// SetGroup в окне между чтением снимка контроллером и мутацией не откатывается —
/// 204, groupId равен значению конкурентной операции, Δkdf=2, refresh-токены
/// отозваны кроме текущего (ISS-002).
/// </summary>
public sealed class MePasswordAtomicityTests(Cr001GateFixture fixture) : IClassFixture<Cr001GateFixture>
{
    private readonly WebApplicationFactory<Program> _factory = fixture.Host;
    private readonly GatedUserRepository _gate = fixture.Gate;

    [Fact]
    public async Task Put_ConcurrentSetGroupBetweenReadAndMutation_GroupNotReverted()
    {
        // given: студент в группе ИК-221; конкурентная цель — ИК-222; refresh-токен
        // текущего запроса и «второго устройства».
        var groupB = Cr001GateHarness.SeedGroup(_factory, "ИК-222");
        var student = Cr001GateHarness.SeedHashedUser(_factory, "me-pwd-atomic", groupId: null);
        var currentDeviceRefresh = Cr001GateHarness.SeedRefreshToken(_factory, student.Id);
        var otherDeviceRefresh = Cr001GateHarness.SeedRefreshToken(_factory, student.Id);
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var users = Cr001GateHarness.Users(_factory);

        var before = Cr001GateHarness.KdfSnapshot(_factory);
        var (arrived, release) = _gate.Arm(student.Id);
        using var client = Cr001GateHarness.CreateSessionClient(
            _factory, student.Id, UserRoles.Student, refreshTokenValue: currentDeviceRefresh);

        // when: PUT /me/password с верным currentPassword «в полёте»; в окне между
        // чтением снимка и мутацией — конкурентный SetGroup напрямую через репозиторий.
        var putTask = client.PutAsync(
            Cr001GateHarness.PasswordEndpoint,
            Cr001GateHarness.Json(
                $$"""{"currentPassword":"{{Cr001GateHarness.TestUserPassword}}","password":"{{Cr001GateHarness.NewPassword}}","confirmPassword":"{{Cr001GateHarness.NewPassword}}"}"""));

        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(SetGroupResult.Success, users.SetGroup(student.Id, groupB.Id, _ => true));
        release.TrySetResult();

        using var response = await putTask;

        // then: 204; Δkdf=2 (verify текущего + hash нового).
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            2,
            Cr001GateHarness.KdfDelta(before, Cr001GateHarness.KdfSnapshot(_factory)));

        // then: группа студента равна значению конкурентной операции (не откатана
        // снимком), пароль сменён.
        var stored = Cr001GateHarness.StoredUser(_factory, student.Id);
        Assert.Equal(groupB.Id, stored.GroupId);
        Assert.NotEqual(student.PasswordHash, stored.PasswordHash);
        Assert.True(hasher.Verify(Cr001GateHarness.NewPassword, stored.PasswordHash, KdfCallers.ChangePassword));

        // then: refresh-токены отозваны кроме текущего (арбитраж ISS-002).
        Assert.NotNull(Cr001GateHarness.RefreshTokenRecord(_factory, currentDeviceRefresh));
        Assert.Null(Cr001GateHarness.RefreshTokenRecord(_factory, otherDeviceRefresh));
    }
}

/// <summary>
/// AC T-204 «Правка профиля не откатывает пароль и группу»: конкурентные
/// SetPassword и SetGroup в окне между чтением и мутацией не откатываются —
/// 200 ProfileDto по свежему снимку (groupName актуален), passwordHash и
/// groupId — от конкурентных операций.
/// </summary>
public sealed class ProfileAtomicityTests(Cr001GateFixture fixture) : IClassFixture<Cr001GateFixture>
{
    private readonly WebApplicationFactory<Program> _factory = fixture.Host;
    private readonly GatedUserRepository _gate = fixture.Gate;

    [Fact]
    public async Task Put_ConcurrentSetPasswordAndSetGroup_PasswordHashAndGroupNotReverted()
    {
        // given: пользователь; конкурентные цели — новый пароль и группа ИК-222.
        var groupB = Cr001GateHarness.SeedGroup(_factory, "ИК-222");
        var user = Cr001GateHarness.SeedHashedUser(_factory, "me-prof-atomic");
        var users = Cr001GateHarness.Users(_factory);

        var before = Cr001GateHarness.KdfSnapshot(_factory);
        var (arrived, release) = _gate.Arm(user.Id);
        using var client = Cr001GateHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT /me/profile с валидными fullName/email «в полёте»; в окне —
        // конкурентные SetPassword и SetGroup напрямую через репозиторий.
        var putTask = client.PutAsync(
            Cr001GateHarness.ProfileEndpoint,
            Cr001GateHarness.Json("""{"fullName":"Новое ФИО","email":"new@example.com"}"""));

        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(users.SetPassword(user.Id, "concurrent-password-hash"));
        Assert.Equal(SetGroupResult.Success, users.SetGroup(user.Id, groupB.Id, _ => true));
        release.TrySetResult();

        using var response = await putTask;

        // then: 200; ProfileDto по СВЕЖЕМУ снимку — groupName актуален
        // (конкурентная группа ИК-222, а не снимочный null/прежняя).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await Cr001GateHarness.ReadJsonAsync(response);
        Assert.Equal("Новое ФИО", body.RootElement.GetProperty("fullName").GetString());
        Assert.Equal("new@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("ИК-222", body.RootElement.GetProperty("groupName").GetString());

        // then: passwordHash и groupId — от конкурентных операций (не откатаны),
        // fullName/email — от запроса; прочие поля прежние.
        var stored = Cr001GateHarness.StoredUser(_factory, user.Id);
        Assert.Equal("concurrent-password-hash", stored.PasswordHash);
        Assert.Equal(groupB.Id, stored.GroupId);
        Assert.Equal("Новое ФИО", stored.FullName);
        Assert.Equal("new@example.com", stored.Email);
        Assert.Equal(user.Login, stored.Login);
        Assert.Equal(user.Role, stored.Role);
        Assert.Equal(user.CreatedAt, stored.CreatedAt);

        // then: /me/profile не выполняет KDF (пароль мутатором не пересчитывается).
        Assert.Equal(
            0,
            Cr001GateHarness.KdfDelta(before, Cr001GateHarness.KdfSnapshot(_factory)));
    }
}
