using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LabsApp.Auth;
using LabsApp.Auth.RateLimiting;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Infrastructure;

/// <summary>
/// Помощники сценариев безопасности батча B-05 (TS-033..TS-037, TS-163..TS-170):
/// эндпоинты /api/v1/auth (IF-007), клиенты с фиксированным транспортным IP
/// (given кейсов, см. <see cref="B05TestClientIpResolver"/>), DI-сид пользователя
/// с реальным PBKDF2-хэшем (ADR-010), разбор Set-Cookie (имена — AuthCoreDefaults,
/// IF-004), формат хранимого PBKDF2-хэша (IF-002) и инспекция внутренних
/// словарей in-memory хранилищ/лимитера через DI (кейсы требуют счёт меток
/// ключа и размера словаря; интерфейсы IF-015/IF-006 перечисления не дают —
/// копия механики инспекции зоны B-08, контракт реализаций не меняется).
/// </summary>
public static class B05SecurityClients
{
    // Контракты IF-007 (эндпоинты кейсов батча).
    public const string RegisterEndpoint = "/api/v1/auth/register";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RecoveryRequestEndpoint = "/api/v1/auth/recovery/request";

    /// <summary>Текст 429-ответа (словарь FR-080, дословно).</summary>
    public const string RateLimitedMessage = "Слишком много попыток. Повторите позже";

    /// <summary>Пароль кейсов батча ('Passw0rd!' — given TS-033/TS-034/TS-035).</summary>
    public const string CasePassword = "Passw0rd!";

    /// <summary>Клиент с cookie-контейнером (поток регистрация → вход → cookie).</summary>
    public static HttpClient CreateClient(B05SecurityWebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    /// <summary>Клиент с фиксированным транспортным IP (given «RemoteIpAddress=…»).</summary>
    public static HttpClient CreateClientWithIp(B05SecurityWebAppFactory factory, string ip)
    {
        var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add(B05TestClientIpResolver.TestIpHeader, ip);
        return client;
    }

    /// <summary>
    /// POST /auth/register {fullName, login, email, password, repeatPassword}
    /// (FR-012; сырой ответ — статус проверяет кейс).
    /// </summary>
    public static Task<HttpResponseMessage> PostRegisterAsync(
        HttpClient client,
        string fullName,
        string login,
        string email,
        string password = CasePassword) =>
        client.PostAsJsonAsync(RegisterEndpoint, new
        {
            fullName,
            login,
            email,
            password,
            repeatPassword = password,
        });

    /// <summary>POST /auth/login {login, password} (сырой ответ — статус проверяет кейс).</summary>
    public static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync(LoginEndpoint, new { login, password });

    /// <summary>
    /// DI-сид пользователя с реальным PBKDF2-хэшем пароля (IF-002/IF-015):
    /// given «пользователь с email … существует» без зависимости от демо-сида.
    /// </summary>
    public static User SeedUser(
        B05SecurityWebAppFactory factory,
        string login,
        string email,
        string fullName,
        string role,
        string password = CasePassword)
    {
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        var clock = factory.Services.GetRequiredService<TimeProvider>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = hasher.Hash(password),
            FullName = fullName,
            Role = role,
            GroupId = null,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        users.Add(user);
        return user;
    }

    /// <summary>DI-сид студента (роль student).</summary>
    public static User SeedStudent(
        B05SecurityWebAppFactory factory,
        string login,
        string email,
        string fullName,
        string password = CasePassword) =>
        SeedUser(factory, login, email, fullName, UserRoles.Student, password);

    /// <summary>Разбирает Set-Cookie ответа в отображение «имя → значение» (до первого «;»).</summary>
    public static IReadOnlyDictionary<string, string> ReadSetCookies(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                var pair = cookie.Split(';', 2)[0];
                var separator = pair.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                result[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
            }
        }

        return result;
    }

    /// <summary>Значение обязательной Set-Cookie по имени; отсутствие — ошибка сценария.</summary>
    public static string RequiredSetCookie(HttpResponseMessage response, string cookieName)
    {
        var cookies = ReadSetCookies(response);
        Assert.True(
            cookies.TryGetValue(cookieName, out var value) && value.Length > 0,
            $"В ответе ожидается Set-Cookie {cookieName}; фактически установлены: [{string.Join(", ", cookies.Keys)}].");
        return value;
    }

    /// <summary>
    /// Хэш хранения refresh-токена (IF-003): SHA-256 hex от UTF-8 значения,
    /// нижний регистр — дословно формат ITokenService.CreateRefreshToken.
    /// </summary>
    public static string RefreshTokenHash(string refreshTokenValue) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshTokenValue))).ToLowerInvariant();

    /// <summary>
    /// Формат хранимой PBKDF2-строки (IF-002/NFR-005): «PBKDF2-SHA256|итерации|salt|hash»
    /// — алгоритм, 600000 итераций, соль ≥16 байт, производный ключ 32 байта.
    /// </summary>
    public static void AssertStoredKdfFormat(string stored, string context)
    {
        var parts = stored.Split('|');
        Assert.True(
            parts.Length == 4,
            $"{context}: формат хэша обязан хранить параметры «PBKDF2-SHA256|итерации|salt|hash», " +
            $"фактически сегментов: {parts.Length}.");
        Assert.True(
            string.Equals(parts[0], "PBKDF2-SHA256", StringComparison.Ordinal),
            $"{context}: ожидался алгоритм PBKDF2-SHA256, фактически «{parts[0]}».");
        Assert.True(
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            && iterations == 600_000,
            $"{context}: ожидалось 600000 итераций PBKDF2 (NFR-005), фактически «{parts[1]}».");

        var salt = Convert.FromBase64String(parts[2]);
        Assert.True(
            salt.Length >= 16,
            $"{context}: соль обязана быть ≥16 байт (NFR-005), фактически {salt.Length} байт.");

        var derived = Convert.FromBase64String(parts[3]);
        Assert.True(
            derived.Length == 32,
            $"{context}: производный ключ обязан быть 32 байта (IF-002), фактически {derived.Length} байт.");
    }

    /// <summary>
    /// Число записей кодов восстановления в хранилище тестового хоста (только
    /// чтение; для then «записей RecoveryCode … не создано» кейса TS-166):
    /// интерфейс IF-015 перечисления не даёт, инспекция — по внутреннему словарю
    /// in-memory реализации (копия механики зоны B-08).
    /// </summary>
    public static int CountRecoveryCodeRecords(B05SecurityWebAppFactory factory)
    {
        var repository = factory.Services.GetRequiredService<IRecoveryCodeRepository>();
        var codes = ReadPrivateField<Dictionary<Guid, RecoveryCode>>(
            repository,
            typeof(InMemoryRecoveryCodeRepository),
            "_codes",
            "Инспекция хранилища кодов восстановления");
        return codes.Count;
    }

    /// <summary>
    /// Все хэши refresh-токенов, хранимые тестовым хостом (только чтение; для
    /// then «в хранилище сохранена только SHA-256-запись … открытого значения
    /// токена в хранилище нет» кейса TS-036): интерфейс IF-015 перечисления не
    /// даёт, инспекция — по внутреннему словарю in-memory реализации.
    /// </summary>
    public static IReadOnlyList<string> StoredRefreshTokenHashes(B05SecurityWebAppFactory factory)
    {
        var repository = factory.Services.GetRequiredService<IRefreshTokenRepository>();
        var tokens = ReadPrivateField<Dictionary<Guid, RefreshToken>>(
            repository,
            typeof(InMemoryRefreshTokenRepository),
            "_tokens",
            "Инспекция хранилища refresh-токенов");
        return tokens.Values.Select(token => token.TokenHash).ToList();
    }

    /// <summary>
    /// Снимок словаря движка лимитера регистраций: ключ IP → число меток (для
    /// then «в словаре по ключу 10.0.0.1 ровно 5 меток» кейса TS-163 и проверки
    /// ключей XFF-кейсов TS-168/TS-169): прикладной лимитер раскрывает только
    /// TrackedKeysCount, пер-ключевая инспекция — по внутреннему словарю движка.
    /// </summary>
    public static IReadOnlyDictionary<string, int> RegisterLimiterWindowMarks(B05SecurityWebAppFactory factory)
    {
        var limiter = factory.Services.GetRequiredService<RegisterLimiter>();

        var engineProperty = typeof(ApplicationRateLimiter).GetProperty(
            "Engine",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(
            engineProperty is not null,
            "Инспекция лимитера регистраций: свойство Engine не найдено у ApplicationRateLimiter.");
        var engine = engineProperty!.GetValue(limiter) as SlidingWindowLimiter;
        Assert.True(
            engine is not null,
            "Инспекция лимитера регистраций: Engine не является SlidingWindowLimiter.");

        var windows = ReadPrivateField<Dictionary<string, List<long>>>(
            engine!,
            typeof(SlidingWindowLimiter),
            "_windows",
            "Инспекция движка лимитера регистраций");

        return windows.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);
    }

    private static T ReadPrivateField<T>(object instance, Type declaringType, string fieldName, string context)
        where T : class
    {
        var field = declaringType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(
            field is not null,
            $"{context}: поле {fieldName} не найдено в {declaringType.Name}.");
        var value = field!.GetValue(instance) as T;
        Assert.True(
            value is not null,
            $"{context}: поле {fieldName} типа {declaringType.Name} равно null или другого типа.");
        return value!;
    }
}
