using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Auth.RateLimiting;
using LabsApp.Controllers;
using LabsApp.Domain.Entities;
using LabsApp.Hosting;
using LabsApp.Hosting.Configuration;
using LabsApp.Observability;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;
using LabsApp.Tests.Hosting;
using LabsApp.Tests.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.Tests.Auth;

// ============================================================================
// Эндпойнт-тесты AuthController (C-005, IF-007; FR-006/007/008/009/010/011 +
// KDF-гейты FR-027). Порядок «register первым» (ADR-033): сценарии TS-028..036
// закрываются здесь. Хост — TestWebAppFactory без демо-набора с
// Auth__Pbkdf2Iterations=1000 (контур B-03) и FakeTimeProvider вместо
// TimeProvider.System (ADR-002): окна лимитеров и TTL токенов детерминированы.
// Гейты Δkdf — снимки IKdfCounter.Snapshot() до/после запроса (FR-027, BUG-002).
//
// Один хост на всю группу (ICollectionFixture): состояние лимитеров общее,
// поэтому КАЖДЫЙ тест перед окном попыток сдвигает часы за пределы окна
// (2 ч — регистрация, 2 мин — вход); поведение «429/успех не пишет метку»
// проверяется ПРЯМОЙ инспекцией IRateLimitStore (CR-001): скольжение окна
// такую регрессию не ловит — все запросы сценария идут при одном замороженном
// времени, метка ветки 429/успеха получила бы таймстамп t0 и вычистилась бы
// вместе с сидированными. Классы в
// одной коллекции исполняются последовательно. Учётные записи для login-веток
// сидуются DI через IPasswordHasher (ADR-015): сид-учётки 'teacher' хэшируются
// тем же IPasswordHasher (метка seed, T-005), login-матрица проверяется на
// собственной учётке.
// ============================================================================

/// <summary>Общая коллекция auth-эндпойнтов: один хост с общим FakeTimeProvider.</summary>
[CollectionDefinition("AuthEndpoints")]
public sealed class AuthEndpointsCollection : ICollectionFixture<AuthApiFactory>
{
    public const string Name = "AuthEndpoints";
}

/// <summary>
/// Фикстура auth-эндпойнтов: TestWebAppFactory без демо-набора,
/// Auth__Pbkdf2Iterations=1000 (контур B-03); TimeProvider подменяется
/// <see cref="FakeTimeProvider"/> — регистрация добавляется позже регистраций
/// Program, поэтому выигрывает последняя.
/// </summary>
public sealed class AuthApiFactory : IDisposable
{
    /// <summary>Единый источник бизнес-времени тестового хоста (ADR-002).</summary>
    public FakeTimeProvider Clock { get; } = new();

    private readonly TestWebAppFactory _root = new(
        null,
        new Dictionary<string, string?>
        {
            [SeedOptions.DemoDataVariable] = "false",
            [AuthOptions.Pbkdf2IterationsVariable] = "1000",
        });

    private WebApplicationFactory<Program>? _factory;

    /// <summary>Хост с FakeTimeProvider: окна лимитеров и TTL токенов детерминированы.
    /// Отключение вотчеров конфигурации (hostBuilder:reloadConfigOnChange=false —
    /// обязательный в этой среде образец B03HostFactory/B06WebAppFactory):
    /// пер-пользовательский лимит inotify-экземпляров (128) исчерпывается
    /// параллельными WAF-хостами, IOException на старте хоста делает прогон
    /// недетерминированным.</summary>
    public WebApplicationFactory<Program> Factory =>
        _factory ??= _root.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Clock));
        });

    /// <inheritdoc/>
    public void Dispose()
    {
        _factory?.Dispose();
        _root.Dispose();
    }
}

/// <summary>
/// Харнес auth-эндпойнтов: анонимные клиенты (cookie переносит сценарий),
/// DI-сид пользователей/групп/refresh-сессий, снимки IKdfCounter (гейты Δkdf,
/// FR-027) и разбор конверта ошибок IF-001.
/// </summary>
internal static class AuthEndpointHarness
{
    public const string RegisterEndpoint = "/api/v1/auth/register";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RefreshEndpoint = "/api/v1/auth/refresh";
    public const string LogoutEndpoint = "/api/v1/auth/logout";
    public const string MeEndpoint = "/api/v1/auth/me";

    public const string ValidPassword = "Passw0rd!";

    /// <summary>Сдвиг времени, полностью опустошающий окно регистраций (1 час).</summary>
    public static readonly TimeSpan RegisterWindowStep = TimeSpan.FromHours(2);

    /// <summary>Сдвиг времени, полностью опустошающий окно неудач входа (60 с).</summary>
    public static readonly TimeSpan FailureWindowStep = TimeSpan.FromMinutes(2);

    public static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    public static HttpClient CreateSessionClient(WebApplicationFactory<Program> factory, Guid userId, string role)
    {
        var token = factory.Services
            .GetRequiredService<ITokenService>()
            .IssueAccessToken(userId, role);

        var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={token}");
        return client;
    }

    /// <summary>DI-сид пользователя с паролем, хэшированным хэшером хоста (метка seed).</summary>
    public static User SeedUser(
        WebApplicationFactory<Program> factory,
        string login,
        string password,
        string role = UserRoles.Student,
        Guid? groupId = null,
        string? email = null,
        string? fullName = null)
    {
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        var repository = factory.Services.GetRequiredService<IUserRepository>();
        repository.Add(new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email ?? $"{login}@example.com",
            PasswordHash = hasher.Hash(password, KdfCallers.Seed),
            FullName = fullName ?? "Тест Тестович Тестов",
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
        repository.Add(new Group { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTime.UtcNow });
        return repository.GetByName(name)
            ?? throw new InvalidOperationException($"Группа {name} не сохранилась при DI-сиде.");
    }

    public static void RenameGroup(WebApplicationFactory<Program> factory, Group group, string newName)
    {
        group.Name = newName;
        factory.Services.GetRequiredService<IGroupRepository>().Update(group);
    }

    /// <summary>DI-минт refresh-сессии: значение для cookie, в хранилище — только хэш.</summary>
    public static (RefreshTokenGrant Grant, RefreshToken Record) SeedRefreshSession(
        WebApplicationFactory<Program> factory,
        Guid userId)
    {
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        var grant = tokens.CreateRefreshToken(userId);
        var record = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = grant.TokenHash,
            ExpiresAt = grant.ExpiresAt,
            CreatedAt = DateTime.UtcNow,
        };
        factory.Services.GetRequiredService<ISecurityTokenRepository>().Add(record);
        return (grant, record);
    }

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public static async Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client,
        string endpoint,
        string json)
    {
        return await client.PostAsync(endpoint, Json(json));
    }

    /// <summary>Ровно <paramref name="times"/> неудачных входов через HTTP: метки пишутся тем
    /// же кодом, что и в сценарии (ключ «lower(trim(login))|IP» консистентен).</summary>
    public static async Task FailLoginsAsync(HttpClient client, string login, string password, int times)
    {
        for (var attempt = 0; attempt < times; attempt++)
        {
            using var response = await PostJsonAsync(
                client,
                LoginEndpoint,
                $$"""{"login":"{{login}}","password":"{{password}}}"}""");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // ------------------------------------------------------------------
    // Инспекция лимитера неудачных входов (FR-007, CR-001): прямое чтение
    // состояния IRateLimitStore вместо вывода через скольжение окна.
    // ------------------------------------------------------------------

    /// <summary>Ключ лимитера неудачных входов «lower(trim(login))|IP»
    /// (зеркало LoginFailureLimiter.BuildKey).</summary>
    public static string LoginFailureKey(string login, string ip) =>
        LimiterKeys.FromLogin(login) + "|" + LimiterKeys.FromIp(ip);

    /// <summary>Копия меток ключа лимитера входа из хранилища хоста (прямая
    /// инспекция IRateLimitStore); null — записи нет. Копия обязательна:
    /// TryGetMarks отдаёт живой список хранилища, а не снимок.</summary>
    public static IReadOnlyList<long>? LoginFailureMarks(
        WebApplicationFactory<Program> factory,
        string login,
        string ip)
    {
        var store = factory.Services.GetRequiredService<IRateLimitStore>();
        return store.TryGetMarks(RateLimitPolicies.Login, LoginFailureKey(login, ip), out var marks)
            ? marks.ToArray()
            : null;
    }

    // ------------------------------------------------------------------
    // Гейты Δkdf (FR-027): снимки до/после + дельты.
    // ------------------------------------------------------------------

    public static IReadOnlyDictionary<string, long> KdfSnapshot(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IKdfCounter>().Snapshot();

    public static long TotalDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after) =>
        after.Values.Sum() - before.Values.Sum();

    public static long CallerDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after,
        string caller) =>
        (after.TryGetValue(caller, out var afterValue) ? afterValue : 0)
        - (before.TryGetValue(caller, out var beforeValue) ? beforeValue : 0);

    // ------------------------------------------------------------------
    // Разбор Set-Cookie и конверта ошибок (IF-001/NFR-007).
    // ------------------------------------------------------------------

    public static string[] SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];

    /// <summary>Set-Cookie конкретной cookie; пары «имя=» в значениях не встречаются (JWT/opaque).</summary>
    public static string SetCookieOf(string[] setCookies, string name) =>
        setCookies.Single(setCookie => setCookie.StartsWith($"{name}=", StringComparison.Ordinal));

    /// <summary>Пара «имя=значение» из Set-Cookie (до первого «;»).</summary>
    public static string NameValue(string setCookie) => setCookie.Split(';', 2)[0];

    /// <summary>Атрибуты NFR-007: HttpOnly, SameSite=Strict, Path=/, Max-Age; Development — без Secure.</summary>
    public static void AssertAuthCookieAttributes(string setCookie, string maxAge)
    {
        var separator = setCookie.IndexOf(';');
        Assert.True(separator > 0, "Ожидались атрибуты Set-Cookie.");
        var attributes = setCookie[(separator + 1)..];

        Assert.Contains("path=/", attributes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", attributes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", attributes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"max-age={maxAge}", attributes, StringComparison.OrdinalIgnoreCase);
        // Development-хост: Secure отсутствует (FR-008 «Secure вне Development»).
        Assert.DoesNotContain("secure", attributes, StringComparison.OrdinalIgnoreCase);
    }

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

    /// <summary>Свойство errors 400 (клон: элемент живёт после dispose документа).</summary>
    public static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        using var body = await ReadJsonAsync(response);
        Assert.True(
            body.RootElement.TryGetProperty("errors", out var errors),
            "Ожидалось свойство errors в теле 400.");
        return errors.Clone();
    }

    public static string[] ErrorOf(JsonElement errors, string field) =>
        [.. errors.GetProperty(field).EnumerateArray().Select(item => item.GetString() ?? string.Empty)];
}

/// <summary>
/// FR-006 (BUG-001/TS-028..036, BUG-004..009): регистрация — 201 MeDto + обе
/// cookie + Δkdf(register)=1; пакетная валидация без KDF; 409 login→email;
/// пробельное ФИО; роль из входа игнорируется; нестроковые поля = пустые
/// строки; границы длины логина; 429 шестой попытки ДО разбора тела.
/// </summary>
[Collection("AuthEndpoints")]
public sealed class AuthRegisterEndpointTests(AuthApiFactory fixture)
{
    private readonly AuthApiFactory _fixture = fixture;

    private WebApplicationFactory<Program> Factory => _fixture.Factory;

    [Fact]
    public async Task Register_ValidFields_Returns201MeDtoWithCookiesAndSingleKdf()
    {
        // given: окно регистраций свободно; счётчик KDF снят.
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        var before = AuthEndpointHarness.KdfSnapshot(Factory);

        // when: POST /auth/register с валидными значениями.
        using var client = AuthEndpointHarness.CreateClient(Factory);
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            """{"fullName":"Иванов Иван","login":"newuser","email":"nu@example.com","password":"Passw0rd!","repeatPassword":"Passw0rd!"}""");

        // then: 201 MeDto role='student', groupName=null; Δkdf(register)=1 (BUG-001/TS-028).
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = await AuthEndpointHarness.ReadJsonAsync(response);
        Assert.Equal("newuser", body.RootElement.GetProperty("login").GetString());
        Assert.Equal("Иванов Иван", body.RootElement.GetProperty("fullName").GetString());
        Assert.Equal(UserRoles.Student, body.RootElement.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("groupName").ValueKind);

        var after = AuthEndpointHarness.KdfSnapshot(Factory);
        Assert.Equal(1, AuthEndpointHarness.TotalDelta(before, after));
        Assert.Equal(1, AuthEndpointHarness.CallerDelta(before, after, KdfCallers.Register));

        // then: NFR-007 — ровно 2 Set-Cookie с атрибутами 900/604800.
        var setCookies = AuthEndpointHarness.SetCookies(response);
        Assert.Equal(2, setCookies.Length);
        AuthEndpointHarness.AssertAuthCookieAttributes(
            AuthEndpointHarness.SetCookieOf(setCookies, AuthCoreDefaults.AccessTokenCookieName),
            "900");
        AuthEndpointHarness.AssertAuthCookieAttributes(
            AuthEndpointHarness.SetCookieOf(setCookies, AuthCoreDefaults.RefreshTokenCookieName),
            "604800");

        // then: пользователь создан student'ом без группы.
        var stored = Factory.Services.GetRequiredService<IUserRepository>().GetByLogin("newuser");
        Assert.NotNull(stored);
        Assert.Equal(UserRoles.Student, stored.Role);
        Assert.Null(stored.GroupId);
    }

    [Fact]
    public async Task Register_RoleFieldFromInput_IsIgnored_CreatesStudent()
    {
        // given: валидные поля; логин/email свободны (BUG-007/TS-034).
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        using var client = AuthEndpointHarness.CreateClient(Factory);

        // when: POST с лишним полем role:'teacher'.
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            """{"fullName":"Роль Игнорируется","login":"role-hacker","email":"role-hacker@example.com","password":"Passw0rd!","repeatPassword":"Passw0rd!","role":"teacher"}""");

        // then: 201; роль в ответе и хранилище — student.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await AuthEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(UserRoles.Student, body.RootElement.GetProperty("role").GetString());
        Assert.Equal(
            UserRoles.Student,
            Factory.Services.GetRequiredService<IUserRepository>().GetByLogin("role-hacker")!.Role);
    }

    [Fact]
    public async Task Register_PasswordBoundary_128Created_129OnlyMaxError()
    {
        // given: окно регистраций свободно (ISS-016).
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        using var client = AuthEndpointHarness.CreateClient(Factory);

        // when: пароль длиной ровно 128 (буквы, цифра, спецзнак).
        using var valid = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            $$"""{"fullName":"Граница Пароля","login":"pw-max","email":"pw-max@example.com","password":"{{new string('a', 126)}}1!","repeatPassword":"{{new string('a', 126)}}1!"}""");

        // then: 201 (Δkdf=1 — пароль принят).
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);

        // when: пароль длиной 129.
        var before = AuthEndpointHarness.KdfSnapshot(Factory);
        using var invalid = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            $$"""{"fullName":"Граница Пароля","login":"pw-over","email":"pw-over@example.com","password":"{{new string('a', 127)}}1!","repeatPassword":"{{new string('a', 127)}}1!"}""");

        // then: 400; единственная ошибка password.max; пользователь не создан; Δkdf=0.
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = await AuthEndpointHarness.ErrorsAsync(invalid);
        Assert.Equal(
            ["Пароль — не более 128 символов"],
            AuthEndpointHarness.ErrorOf(errors, "password"));
        Assert.Single(errors.EnumerateObject());
        Assert.Null(Factory.Services.GetRequiredService<IUserRepository>().GetByLogin("pw-over"));
        Assert.Equal(
            0,
            AuthEndpointHarness.TotalDelta(before, AuthEndpointHarness.KdfSnapshot(Factory)));
    }

    [Fact]
    public async Task Register_LoginLengthBoundary_100Created_101LengthError()
    {
        // given: окно регистраций свободно (BUG-009/TS-036).
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        using var client = AuthEndpointHarness.CreateClient(Factory);

        // when: логин длиной ровно 100.
        using var valid = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            $$"""{"fullName":"Граница Логина","login":"{{new string('x', 100)}}","email":"login-max@example.com","password":"Passw0rd!","repeatPassword":"Passw0rd!"}""");

        // then: 201.
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);

        // when: логин длиной 101.
        using var invalid = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            $$"""{"fullName":"Граница Логина","login":"{{new string('y', 101)}}","email":"login-over@example.com","password":"Passw0rd!","repeatPassword":"Passw0rd!"}""");

        // then: 400; errors.login=['Логин — от 1 до 100 символов'].
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = await AuthEndpointHarness.ErrorsAsync(invalid);
        Assert.Equal(
            ["Логин — от 1 до 100 символов"],
            AuthEndpointHarness.ErrorOf(errors, "login"));
    }

    [Fact]
    public async Task Register_InvalidFields_ReturnsAllErrorsAtOnce_WithoutKdf()
    {
        // given: любое состояние хранилища (BUG-002/TS-029).
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        using var client = AuthEndpointHarness.CreateClient(Factory);
        var before = AuthEndpointHarness.KdfSnapshot(Factory);

        // when: кириллица в логине, битый email, короткий пароль, другой повтор.
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            """{"fullName":"Пакет Ошибок","login":"иван","email":"abc","password":"abc","repeatPassword":"xyz"}""");

        // then: 400 «Данные заполнены неверно»; все ошибки сразу; Δkdf=0; пользователь не создан.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await AuthEndpointHarness.MessageAsync(response));
        var errors = await AuthEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            ["Логин может содержать только латинские буквы, цифры, точку, дефис и подчёркивание"],
            AuthEndpointHarness.ErrorOf(errors, "login"));
        Assert.Equal(["Введите корректный email"], AuthEndpointHarness.ErrorOf(errors, "email"));
        Assert.Equal(
            [
                "Пароль должен содержать не менее 8 символов",
                "Пароль должен содержать хотя бы одну цифру",
                "Пароль должен содержать хотя бы один специальный знак",
            ],
            AuthEndpointHarness.ErrorOf(errors, "password"));
        Assert.Equal(["Пароли не совпадают"], AuthEndpointHarness.ErrorOf(errors, "repeatPassword"));
        Assert.Equal(4, errors.EnumerateObject().Count());

        Assert.Equal(0, AuthEndpointHarness.TotalDelta(before, AuthEndpointHarness.KdfSnapshot(Factory)));
        Assert.Null(Factory.Services.GetRequiredService<IUserRepository>().GetByLogin("иван"));
    }

    [Fact]
    public async Task Register_DuplicateLogin_CheckedBeforeEmail()
    {
        // given: существуют login 'stu' и email 'stu@example.com' (BUG-004/TS-031).
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        AuthEndpointHarness.SeedUser(Factory, "stu", AuthEndpointHarness.ValidPassword, email: "stu@example.com");
        using var client = AuthEndpointHarness.CreateClient(Factory);
        var before = AuthEndpointHarness.KdfSnapshot(Factory);

        // when: регистрация с занятым логином (в другом регистре) и тем же email.
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            """{"fullName":"Дубликат Логина","login":"STU","email":"stu@example.com","password":"Passw0rd!","repeatPassword":"Passw0rd!"}""");

        // then: 409 именно про логин (email не проверялся); Δkdf=0.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Пользователь с таким логином уже существует",
            await AuthEndpointHarness.MessageAsync(response));
        Assert.Equal(0, AuthEndpointHarness.TotalDelta(before, AuthEndpointHarness.KdfSnapshot(Factory)));
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsEmailConflict()
    {
        // given: email 'a@b.ru' занят другим пользователем (BUG-005/TS-032).
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        AuthEndpointHarness.SeedUser(Factory, "email-owner", AuthEndpointHarness.ValidPassword, email: "a@b.ru");
        using var client = AuthEndpointHarness.CreateClient(Factory);
        var before = AuthEndpointHarness.KdfSnapshot(Factory);

        // when: регистрация со свободным логином и занятым email в другом регистре.
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            """{"fullName":"Дубликат Email","login":"email-second","email":"A@B.RU","password":"Passw0rd!","repeatPassword":"Passw0rd!"}""");

        // then: 409 «Пользователь с таким email уже существует»; Δkdf=0.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Пользователь с таким email уже существует",
            await AuthEndpointHarness.MessageAsync(response));
        Assert.Equal(0, AuthEndpointHarness.TotalDelta(before, AuthEndpointHarness.KdfSnapshot(Factory)));
        Assert.Null(Factory.Services.GetRequiredService<IUserRepository>().GetByLogin("email-second"));
    }

    [Fact]
    public async Task Register_WhitespaceFullName_ReturnsRequiredError()
    {
        // given: прочие поля валидны (BUG-006/TS-033).
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        using var client = AuthEndpointHarness.CreateClient(Factory);

        // when: fullName из одних пробелов.
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            """{"fullName":"   ","login":"space-name","email":"space-name@example.com","password":"Passw0rd!","repeatPassword":"Passw0rd!"}""");

        // then: 400; errors.fullName=['Заполните поле'].
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await AuthEndpointHarness.MessageAsync(response));
        var errors = await AuthEndpointHarness.ErrorsAsync(response);
        Assert.Equal(["Заполните поле"], AuthEndpointHarness.ErrorOf(errors, "fullName"));
    }

    [Fact]
    public async Task Register_NonStringFields_AreTreatedAsEmptyStrings()
    {
        // given: — (BUG-008/TS-035).
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        using var client = AuthEndpointHarness.CreateClient(Factory);

        // when: нестроковые fullName/login/email и строковые пароли.
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            """{"fullName":123,"login":null,"email":true,"password":"Passw0rd!","repeatPassword":"Passw0rd!"}""");

        // then: 400; требуемые ошибки ровно по трём полям (пароль валиден, повторы равны).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await AuthEndpointHarness.MessageAsync(response));
        var errors = await AuthEndpointHarness.ErrorsAsync(response);
        Assert.Equal(["Заполните поле"], AuthEndpointHarness.ErrorOf(errors, "fullName"));
        Assert.Equal(["Заполните поле"], AuthEndpointHarness.ErrorOf(errors, "login"));
        Assert.Equal(["Заполните поле"], AuthEndpointHarness.ErrorOf(errors, "email"));
        Assert.Equal(3, errors.EnumerateObject().Count());
    }

    [Fact]
    public async Task Register_NonStringPassword_WithRepeatPassword_MismatchInErrorBatch()
    {
        // given: — (BUG-002/TS-208: password — JSON-число → пустая строка).
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        using var client = AuthEndpointHarness.CreateClient(Factory);
        var before = AuthEndpointHarness.KdfSnapshot(Factory);

        // when: password нестроковый, repeatPassword — непустая строка.
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            """{"fullName":"Нестроковый Пароль","login":"ns-user","email":"ns-user@example.com","password":12345,"repeatPassword":"Passw0rd!"}""");

        // then: 400; errors.password — ровно 4 текста (min/digit/letter/special);
        // errors.repeatPassword=['Пароли не совпадают'] — дословное равенство с
        // пустым паролем нарушено; пользователь не создан; Δkdf=0.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await AuthEndpointHarness.MessageAsync(response));
        var errors = await AuthEndpointHarness.ErrorsAsync(response);
        Assert.Equal(
            [
                "Пароль должен содержать не менее 8 символов",
                "Пароль должен содержать хотя бы одну цифру",
                "Пароль должен содержать хотя бы одну букву",
                "Пароль должен содержать хотя бы один специальный знак",
            ],
            AuthEndpointHarness.ErrorOf(errors, "password"));
        Assert.Equal(["Пароли не совпадают"], AuthEndpointHarness.ErrorOf(errors, "repeatPassword"));

        Assert.Equal(0, AuthEndpointHarness.TotalDelta(before, AuthEndpointHarness.KdfSnapshot(Factory)));
        Assert.Null(Factory.Services.GetRequiredService<IUserRepository>().GetByLogin("ns-user"));
    }

    [Fact]
    public async Task Register_MissingOrWhitespaceRepeatPassword_IsRequiredError_WithoutKdf()
    {
        // given: прочие поля валидны; repeatPassword отсутствует либо пробельный
        // (IF-007: поле обязателен — «Заполните поле», зеркало requiredTrim клиента).
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        using var client = AuthEndpointHarness.CreateClient(Factory);
        var before = AuthEndpointHarness.KdfSnapshot(Factory);

        // when: POST без repeatPassword и POST с повтором из одних пробелов.
        using var missing = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            """{"fullName":"Повтор Отсутствует","login":"repeat-missing","email":"repeat-missing@example.com","password":"Passw0rd!"}""");
        using var blank = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            """{"fullName":"Повтор Пробельный","login":"repeat-blank","email":"repeat-blank@example.com","password":"Passw0rd!","repeatPassword":"   "}""");

        // then: обе — 400 с единственной ошибкой repeatPassword required;
        // пользователь не создан; Δkdf=0.
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal("Данные заполнены неверно", await AuthEndpointHarness.MessageAsync(missing));
        Assert.Equal("Данные заполнены неверно", await AuthEndpointHarness.MessageAsync(blank));
        var missingErrors = await AuthEndpointHarness.ErrorsAsync(missing);
        Assert.Equal(["Заполните поле"], AuthEndpointHarness.ErrorOf(missingErrors, "repeatPassword"));
        Assert.Single(missingErrors.EnumerateObject());
        var blankErrors = await AuthEndpointHarness.ErrorsAsync(blank);
        Assert.Equal(["Заполните поле"], AuthEndpointHarness.ErrorOf(blankErrors, "repeatPassword"));
        Assert.Single(blankErrors.EnumerateObject());

        var repository = Factory.Services.GetRequiredService<IUserRepository>();
        Assert.Null(repository.GetByLogin("repeat-missing"));
        Assert.Null(repository.GetByLogin("repeat-blank"));
        Assert.Equal(0, AuthEndpointHarness.TotalDelta(before, AuthEndpointHarness.KdfSnapshot(Factory)));
    }

    [Fact]
    public async Task Register_SixthAttemptWithinHour_IsRateLimitedBeforeBodyParse()
    {
        // given: пять попыток за час исчерпаны (IF-006: считаются ВСЕ попытки).
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        using var client = AuthEndpointHarness.CreateClient(Factory);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var created = await AuthEndpointHarness.PostJsonAsync(
                client,
                AuthEndpointHarness.RegisterEndpoint,
                $$"""{"fullName":"Лимит Регистраций","login":"reg-{{attempt}}","email":"reg-{{attempt}}@example.com","password":"Passw0rd!","repeatPassword":"Passw0rd!"}""");
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var before = AuthEndpointHarness.KdfSnapshot(Factory);

        // when: шестой POST с произвольным (некорректным) телом.
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            "{\"fullName\": \"");

        // then: 429 «Слишком много попыток. Повторите позже»; тело не разбиралось
        // (иначе была бы полевая 400); Δkdf=0.
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(
            "Слишком много попыток. Повторите позже",
            await AuthEndpointHarness.MessageAsync(response));
        Assert.Equal(0, AuthEndpointHarness.TotalDelta(before, AuthEndpointHarness.KdfSnapshot(Factory)));
    }
}

/// <summary>
/// FR-007/FR-027 (SEC-001): KDF-матрица входа — Δkdf=1 в каждой ветке
/// 200/401/429 для известного и неизвестного логина ({} — тоже 1, битый JSON —
/// 400 с Δ=0); успех не опрашивает лимитер и не пишет метку, 429 метку не
/// пишет — прямая инспекция IRateLimitStore (CR-001); неудача пишет метку
/// и блокируется после 5.
/// </summary>
[Collection("AuthEndpoints")]
public sealed class AuthLoginEndpointTests(AuthApiFactory fixture)
{
    private const string KnownLogin = "login-teacher";
    private const string KnownPassword = "teacher123!";
    private const string WrongPassword = "nope123!";

    /// <summary>Фиксированный IP тестового клиента (шов SetClientIp): ключ
    /// «lower(trim(login))|IP» лимитера полностью детерминирован для прямой
    /// инспекции IRateLimitStore и не пересекается с другими тестами хоста.</summary>
    private const string ClientIp = "192.0.2.84";

    private readonly AuthApiFactory _fixture = fixture;

    private WebApplicationFactory<Program> Factory => _fixture.Factory;

    /// <summary>Сдвиг окна + DI-сид известной учётки (идемпотентен: хост общий).</summary>
    private void SeedKnownUser()
    {
        _fixture.Clock.Advance(AuthEndpointHarness.FailureWindowStep);
        if (Factory.Services.GetRequiredService<IUserRepository>().GetByLogin(KnownLogin) is not null)
        {
            return;
        }

        AuthEndpointHarness.SeedUser(
            Factory,
            KnownLogin,
            KnownPassword,
            role: UserRoles.Teacher,
            fullName: "Сидоров Семён Семёнович");
    }

    [Fact]
    public async Task Login_KnownUserCorrectPassword_Returns200MeDtoCookies_SingleKdf()
    {
        // given: известная пара логин/пароль существует; меток для ключа нет.
        SeedKnownUser();
        using var client = AuthEndpointHarness.CreateClient(Factory);
        var before = AuthEndpointHarness.KdfSnapshot(Factory);

        // when: POST /auth/login с верной парой.
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            $$"""{"login":"{{KnownLogin}}","password":"{{KnownPassword}}"}""");

        // then: 200 MeDto {role=teacher, groupName=null}; оба cookie; Δkdf(login)=1.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await AuthEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(KnownLogin, body.RootElement.GetProperty("login").GetString());
        Assert.Equal(UserRoles.Teacher, body.RootElement.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("groupName").ValueKind);

        var after = AuthEndpointHarness.KdfSnapshot(Factory);
        Assert.Equal(1, AuthEndpointHarness.TotalDelta(before, after));
        Assert.Equal(1, AuthEndpointHarness.CallerDelta(before, after, KdfCallers.Login));

        var setCookies = AuthEndpointHarness.SetCookies(response);
        Assert.Equal(2, setCookies.Length);
        AuthEndpointHarness.AssertAuthCookieAttributes(
            AuthEndpointHarness.SetCookieOf(setCookies, AuthCoreDefaults.AccessTokenCookieName),
            "900");
        AuthEndpointHarness.AssertAuthCookieAttributes(
            AuthEndpointHarness.SetCookieOf(setCookies, AuthCoreDefaults.RefreshTokenCookieName),
            "604800");
    }

    [Fact]
    public async Task Login_WrongPassword_KnownUser_Returns401_SingleKdf()
    {
        // given: известная учётка существует; меток для ключа нет.
        SeedKnownUser();
        using var client = AuthEndpointHarness.CreateClient(Factory);

        // when: неверный пароль.
        var before = AuthEndpointHarness.KdfSnapshot(Factory);
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            $$"""{"login":"{{KnownLogin}}","password":"{{WrongPassword}}"}""");

        // then: 401 «Неверный логин или пароль» без cookie; Δkdf=1.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Неверный логин или пароль", await AuthEndpointHarness.MessageAsync(response));
        var after = AuthEndpointHarness.KdfSnapshot(Factory);
        Assert.Equal(1, AuthEndpointHarness.TotalDelta(before, after));
        Assert.Equal(1, AuthEndpointHarness.CallerDelta(before, after, KdfCallers.Login));
        Assert.Empty(AuthEndpointHarness.SetCookies(response));
    }

    [Fact]
    public async Task Login_UnknownUser_ReturnsSame401_ReferenceKdf()
    {
        // given: пользователя 'ghost' не существует.
        SeedKnownUser();
        using var client = AuthEndpointHarness.CreateClient(Factory);

        // when: вход с неизвестным логином.
        var before = AuthEndpointHarness.KdfSnapshot(Factory);
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            """{"login":"ghost","password":"whatever1!"}""");

        // then: тот же 401; Δkdf=1 (эталонная деривация, метка reference).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Неверный логин или пароль", await AuthEndpointHarness.MessageAsync(response));
        var after = AuthEndpointHarness.KdfSnapshot(Factory);
        Assert.Equal(1, AuthEndpointHarness.TotalDelta(before, after));
        Assert.Equal(1, AuthEndpointHarness.CallerDelta(before, after, KdfCallers.Reference));
    }

    [Fact]
    public async Task Login_BlockedKnownUser_Returns429_AfterExactlyOneKdf_NoMark()
    {
        // given: пять меток 'известный логин|IP' в текущем окне (5 неудачных входов);
        // IP клиента зафиксирован швом — ключ лимитера детерминирован (CR-001).
        SeedKnownUser();
        using var client = AuthEndpointHarness.CreateClient(Factory);
        TestWebAppFactory.SetClientIp(client, ClientIp);
        await AuthEndpointHarness.FailLoginsAsync(client, KnownLogin, WrongPassword, times: 5);
        var marksBefore = AuthEndpointHarness.LoginFailureMarks(Factory, KnownLogin, ClientIp);
        Assert.NotNull(marksBefore);
        Assert.Equal(5, marksBefore.Count);

        // when: шестая попытка с неверным паролем.
        var before = AuthEndpointHarness.KdfSnapshot(Factory);
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            $$"""{"login":"{{KnownLogin}}","password":"{{WrongPassword}}"}""");

        // then: 429 «Слишком много попыток. Повторите позже»; Δkdf=1 (KDF до ShouldBlock).
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(
            "Слишком много попыток. Повторите позже",
            await AuthEndpointHarness.MessageAsync(response));
        var after = AuthEndpointHarness.KdfSnapshot(Factory);
        Assert.Equal(1, AuthEndpointHarness.TotalDelta(before, after));
        Assert.Equal(1, AuthEndpointHarness.CallerDelta(before, after, KdfCallers.Login));

        // then: 429 метку НЕ пишет — прямая инспекция IRateLimitStore: ни число,
        // ни значения меток ключа не изменились (вакуумная проверка скольжением
        // окна +61с регрессию не ловила: метка ветки 429 получила бы таймстамп
        // t0 и вычистилась бы вместе с сидированными пятью — CR-001).
        var marksAfter = AuthEndpointHarness.LoginFailureMarks(Factory, KnownLogin, ClientIp);
        Assert.NotNull(marksAfter);
        Assert.Equal(marksBefore, marksAfter);
    }

    [Fact]
    public async Task Login_BlockedUnknownUser_Returns429_AfterExactlyOneKdf()
    {
        // given: пять меток 'ghost|IP' (FR-007: равная стоимость веток 429).
        SeedKnownUser();
        using var client = AuthEndpointHarness.CreateClient(Factory);
        await AuthEndpointHarness.FailLoginsAsync(client, "ghost", "whatever1!", times: 5);

        // when: шестая попытка с неизвестным логином.
        var before = AuthEndpointHarness.KdfSnapshot(Factory);
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            """{"login":"ghost","password":"whatever1!"}""");

        // then: 429; Δkdf=1 (эталонная деривация).
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var after = AuthEndpointHarness.KdfSnapshot(Factory);
        Assert.Equal(1, AuthEndpointHarness.TotalDelta(before, after));
        Assert.Equal(1, AuthEndpointHarness.CallerDelta(before, after, KdfCallers.Reference));
    }

    [Fact]
    public async Task Login_CorrectPasswordOnBlockedKey_Succeeds_WritesNoMark()
    {
        // given: пять меток 'известный логин|IP' — все прошлые неудачи; IP клиента
        // зафиксирован швом — ключ лимитера детерминирован (CR-001).
        SeedKnownUser();
        using var client = AuthEndpointHarness.CreateClient(Factory);
        TestWebAppFactory.SetClientIp(client, ClientIp);
        await AuthEndpointHarness.FailLoginsAsync(client, KnownLogin, WrongPassword, times: 5);
        var marksBefore = AuthEndpointHarness.LoginFailureMarks(Factory, KnownLogin, ClientIp);
        Assert.NotNull(marksBefore);
        Assert.Equal(5, marksBefore.Count);

        // when: вход с ВЕРНЫМ паролем на заблокированном ключе.
        var before = AuthEndpointHarness.KdfSnapshot(Factory);
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            $$"""{"login":"{{KnownLogin}}","password":"{{KnownPassword}}"}""");

        // then: 200 + обе cookie (успех не блокируется); Δkdf=1.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, AuthEndpointHarness.SetCookies(response).Length);
        Assert.Equal(1, AuthEndpointHarness.TotalDelta(before, AuthEndpointHarness.KdfSnapshot(Factory)));

        // then: успех метку НЕ пишет — прямая инспекция IRateLimitStore: ни число,
        // ни значения меток ключа не изменились (вакуумная проверка скольжением
        // окна +61с регрессию не ловила: гипотетическая метка успеха получила бы
        // таймстамп t0 и вычистилась бы вместе с сидированными пятью — CR-001).
        var marksAfter = AuthEndpointHarness.LoginFailureMarks(Factory, KnownLogin, ClientIp);
        Assert.NotNull(marksAfter);
        Assert.Equal(marksBefore, marksAfter);
    }

    [Fact]
    public async Task Login_LoginTrimmedAndCaseInsensitive_Succeeds()
    {
        // given: известная учётка существует; меток для ключа нет.
        SeedKnownUser();
        using var client = AuthEndpointHarness.CreateClient(Factory);

        // when: логин с пробелами и верхним регистром (пробелы снимает Trim
        // контроллера, регистр приводит к нижнему ci-правило репозитория).
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            $$"""{"login":"  {{KnownLogin.ToUpperInvariant()}}  ","password":"{{KnownPassword}}"}""");

        // then: 200 MeDto.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await AuthEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(KnownLogin, body.RootElement.GetProperty("login").GetString());
    }

    [Fact]
    public async Task Login_MissingFields_EmptyValues_ReturnsSingle401_SingleKdf()
    {
        // given: известная учётка существует; пустые значения не проходят проверку (FR-007).
        SeedKnownUser();
        using var client = AuthEndpointHarness.CreateClient(Factory);

        // when: POST {} без полей.
        var before = AuthEndpointHarness.KdfSnapshot(Factory);
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            "{}");

        // then: 401 «Неверный логин или пароль»; Δkdf=1 (равномерная ветка).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Неверный логин или пароль", await AuthEndpointHarness.MessageAsync(response));
        var after = AuthEndpointHarness.KdfSnapshot(Factory);
        Assert.Equal(1, AuthEndpointHarness.TotalDelta(before, after));
    }

    [Fact]
    public async Task Login_LoginWithoutPassword_ReturnsSameSingle401_SingleKdf()
    {
        // given: известная учётка существует; password отсутствует (второй вариант
        // сценария «Отсутствующие поля»: {login} без password — тот же единый 401,
        // что и {}; пустой кандидат проходит ветку проверки с полной стоимостью).
        SeedKnownUser();
        using var client = AuthEndpointHarness.CreateClient(Factory);

        // when: POST с логином, но без password.
        var before = AuthEndpointHarness.KdfSnapshot(Factory);
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            $$"""{"login":"{{KnownLogin}}"}""");

        // then: 401 «Неверный логин или пароль» (единый текст); Δkdf=1.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Неверный логин или пароль", await AuthEndpointHarness.MessageAsync(response));
        var after = AuthEndpointHarness.KdfSnapshot(Factory);
        Assert.Equal(1, AuthEndpointHarness.TotalDelta(before, after));
        Assert.Equal(1, AuthEndpointHarness.CallerDelta(before, after, KdfCallers.Login));
        Assert.Empty(AuthEndpointHarness.SetCookies(response));
    }

    [Fact]
    public async Task Login_MalformedJson_Returns400_WithoutKdf()
    {
        // given: любое состояние хранилища (ошибка зависит только от формата).
        SeedKnownUser();
        using var client = AuthEndpointHarness.CreateClient(Factory);
        var before = AuthEndpointHarness.KdfSnapshot(Factory);

        // when: тело не парсится как JSON.
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            "{bad json");

        // then: 400 «Данные заполнены неверно» БЕЗ errors; Δkdf=0.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Данные заполнены неверно", await AuthEndpointHarness.MessageAsync(response));
        using var body = await AuthEndpointHarness.ReadJsonAsync(response);
        Assert.False(body.RootElement.TryGetProperty("errors", out _), "Битый JSON не отдаёт errors.");
        Assert.Equal(0, AuthEndpointHarness.TotalDelta(before, AuthEndpointHarness.KdfSnapshot(Factory)));
    }

    [Fact]
    public async Task Login_WindowSlides_FailureAfterSixtyOneSeconds_IsNotBlocked()
    {
        // given: пять меток 'slider|IP' записаны в момент t0.
        SeedKnownUser();
        AuthEndpointHarness.SeedUser(Factory, "slider", "slider-pass1!");
        using var client = AuthEndpointHarness.CreateClient(Factory);
        await AuthEndpointHarness.FailLoginsAsync(client, "slider", "wrong-pass1!", times: 4);
        using var fifth = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            """{"login":"slider","password":"wrong-pass1!"}""");
        Assert.Equal(HttpStatusCode.Unauthorized, fifth.StatusCode);

        // when: неудачный вход в t0+61с (окно 60с).
        _fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            """{"login":"slider","password":"wrong-pass1!"}""");

        // then: 401 (метка записана заново), не 429.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Неверный логин или пароль", await AuthEndpointHarness.MessageAsync(response));
    }
}

/// <summary>
/// FR-009/FR-010/FR-011 (BUG-001): refresh — 204 + только новая access-cookie,
/// refresh не ротируется; отказы — 401 «Не авторизован» без Set-Cookie;
/// logout — всегда 204, отзыв живого refresh, оба cookie Max-Age=0, идемпотентен
/// и работает с истёкшим access; me — MeDto с groupName по текущему состоянию,
/// без токена 401.
/// </summary>
[Collection("AuthEndpoints")]
public sealed class AuthSessionEndpointTests(AuthApiFactory fixture)
{
    private readonly AuthApiFactory _fixture = fixture;

    private WebApplicationFactory<Program> Factory => _fixture.Factory;

    private HttpClient ClientWithRefreshCookie(string tokenValue)
    {
        var client = AuthEndpointHarness.CreateClient(Factory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.RefreshTokenCookieName}={tokenValue}");
        return client;
    }

    // ------------------------------------------------------------------
    // POST /auth/refresh (FR-009)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Refresh_ValidCookie_Returns204_NewAccessCookieOnly_RefreshNotRotated()
    {
        // given: валидная refresh-сессия (DI-минт, BUG-001).
        var user = AuthEndpointHarness.SeedUser(Factory, "refresh-ok", AuthEndpointHarness.ValidPassword);
        var (grant, record) = AuthEndpointHarness.SeedRefreshSession(Factory, user.Id);
        using var client = ClientWithRefreshCookie(grant.Value);

        // when: POST /auth/refresh.
        using var response = await client.PostAsync(AuthEndpointHarness.RefreshEndpoint, content: null);

        // then: 204; ровно один Set-Cookie — access_token (refresh не переустановлен);
        // запись хранилища по тому же хэшу жива (без ротации).
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var setCookies = AuthEndpointHarness.SetCookies(response);
        Assert.Single(setCookies);
        var accessCookie = AuthEndpointHarness.SetCookieOf(setCookies, AuthCoreDefaults.AccessTokenCookieName);
        AuthEndpointHarness.AssertAuthCookieAttributes(accessCookie, "900");
        Assert.DoesNotContain(AuthCoreDefaults.RefreshTokenCookieName, setCookies[0], StringComparison.Ordinal);
        Assert.NotNull(
            Factory.Services.GetRequiredService<ISecurityTokenRepository>().FindLiveByHash(record.TokenHash));

        // then: новый access работает — GET /auth/me от имени пользователя.
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", AuthEndpointHarness.NameValue(accessCookie));
        using var me = await client.GetAsync(AuthEndpointHarness.MeEndpoint);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var meBody = await AuthEndpointHarness.ReadJsonAsync(me);
        Assert.Equal("refresh-ok", meBody.RootElement.GetProperty("login").GetString());
    }

    [Fact]
    public async Task Refresh_WithoutCookie_Returns401_NoSetCookie()
    {
        // given: запрос без cookie refresh_token.
        using var client = AuthEndpointHarness.CreateClient(Factory);

        // when: POST /auth/refresh.
        using var response = await client.PostAsync(AuthEndpointHarness.RefreshEndpoint, content: null);

        // then: 401 «Не авторизован»; cookie не выставляются.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await AuthEndpointHarness.MessageAsync(response));
        Assert.Empty(AuthEndpointHarness.SetCookies(response));
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401_NoSetCookie()
    {
        // given: cookie со значением, для которого записи по SHA-256 нет.
        using var client = ClientWithRefreshCookie("totally-unknown-refresh-value");

        // when: POST /auth/refresh.
        using var response = await client.PostAsync(AuthEndpointHarness.RefreshEndpoint, content: null);

        // then: 401 без Set-Cookie.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(AuthEndpointHarness.SetCookies(response));
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Returns401_NoSetCookie()
    {
        // given: сессия с expiresAt через 7 дней; часы переведены за TTL.
        var user = AuthEndpointHarness.SeedUser(Factory, "refresh-expired", AuthEndpointHarness.ValidPassword);
        var (grant, _) = AuthEndpointHarness.SeedRefreshSession(Factory, user.Id);
        _fixture.Clock.Advance(TimeSpan.FromDays(8));
        using var client = ClientWithRefreshCookie(grant.Value);

        // when: POST /auth/refresh.
        using var response = await client.PostAsync(AuthEndpointHarness.RefreshEndpoint, content: null);

        // then: 401 «Не авторизован»; без Set-Cookie.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await AuthEndpointHarness.MessageAsync(response));
        Assert.Empty(AuthEndpointHarness.SetCookies(response));
    }

    [Fact]
    public async Task Refresh_RevokedToken_Returns401_NoSetCookie()
    {
        // given: сессия, отозванная через хранилище (модель logout).
        var user = AuthEndpointHarness.SeedUser(Factory, "refresh-revoked", AuthEndpointHarness.ValidPassword);
        var (grant, record) = AuthEndpointHarness.SeedRefreshSession(Factory, user.Id);
        Factory.Services.GetRequiredService<ISecurityTokenRepository>().Revoke(record.Id);
        using var client = ClientWithRefreshCookie(grant.Value);

        // when: POST /auth/refresh с тем же токеном.
        using var response = await client.PostAsync(AuthEndpointHarness.RefreshEndpoint, content: null);

        // then: 401 «Не авторизован».
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await AuthEndpointHarness.MessageAsync(response));
        Assert.Empty(AuthEndpointHarness.SetCookies(response));
    }

    // ------------------------------------------------------------------
    // POST /auth/logout (FR-010)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Logout_WithSession_Returns204_RevokesRefresh_ClearsBothCookies()
    {
        // given: пользователь зарегистрирован через HTTP (обе cookie в контейнере).
        _fixture.Clock.Advance(AuthEndpointHarness.RegisterWindowStep);
        using var client = AuthEndpointHarness.CreateClient(Factory);
        using var registered = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.RegisterEndpoint,
            """{"fullName":"Выход Со Сессией","login":"logout-user","email":"logout-user@example.com","password":"Passw0rd!","repeatPassword":"Passw0rd!"}""");
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var container = new TestCookieContainer();
        container.CaptureFrom(registered);
        Assert.True(container.Contains(AuthCoreDefaults.RefreshTokenCookieName));

        // when: POST /auth/logout с обеими cookie.
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpointHarness.LogoutEndpoint);
        container.ApplyTo(request);
        using var logout = await client.SendAsync(request);

        // then: 204; оба cookie сброшены (Max-Age=0); refresh-токен отозван —
        // повторный /auth/refresh со СТАРЫМ значением даёт 401.
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var setCookies = AuthEndpointHarness.SetCookies(logout);
        Assert.Equal(2, setCookies.Length);
        AuthEndpointHarness.AssertAuthCookieAttributes(
            AuthEndpointHarness.SetCookieOf(setCookies, AuthCoreDefaults.AccessTokenCookieName),
            "0");
        AuthEndpointHarness.AssertAuthCookieAttributes(
            AuthEndpointHarness.SetCookieOf(setCookies, AuthCoreDefaults.RefreshTokenCookieName),
            "0");

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, AuthEndpointHarness.RefreshEndpoint);
        container.ApplyTo(refreshRequest);
        using var refresh = await client.SendAsync(refreshRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutCookies_IsIdempotent_Always204()
    {
        // given: запрос без cookie access_token и refresh_token.
        using var client = AuthEndpointHarness.CreateClient(Factory);

        // when: два logout подряд.
        using var first = await client.PostAsync(AuthEndpointHarness.LogoutEndpoint, content: null);
        using var second = await client.PostAsync(AuthEndpointHarness.LogoutEndpoint, content: null);

        // then: оба — 204 без ошибок; оба cookie Max-Age=0 в каждом ответе.
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(2, AuthEndpointHarness.SetCookies(first).Length);
        Assert.Equal(2, AuthEndpointHarness.SetCookies(second).Length);
    }

    [Fact]
    public async Task Logout_ExpiredAccessWithLiveRefresh_Returns204_AndRevokes()
    {
        // given: access невалиден (мусор в cookie), refresh жив.
        var user = AuthEndpointHarness.SeedUser(Factory, "logout-stale", AuthEndpointHarness.ValidPassword);
        var (grant, _) = AuthEndpointHarness.SeedRefreshSession(Factory, user.Id);
        using var client = AuthEndpointHarness.CreateClient(Factory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}=not-a-jwt; {AuthCoreDefaults.RefreshTokenCookieName}={grant.Value}");

        // when: POST /auth/logout (валидный access не требуется).
        using var logout = await client.PostAsync(AuthEndpointHarness.LogoutEndpoint, content: null);

        // then: 204; refresh отозван — последующий refresh с тем же значением 401.
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var refresh = await client.PostAsync(AuthEndpointHarness.RefreshEndpoint, content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    // ------------------------------------------------------------------
    // GET /auth/me (FR-011)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Me_StudentWithGroup_ReturnsMeDtoWithCurrentGroupName()
    {
        // given: студент в группе; группа переименована ПОСЛЕ сида.
        var group = AuthEndpointHarness.SeedGroup(Factory, "ИК-Ме");
        var user = AuthEndpointHarness.SeedUser(
            Factory,
            "me-student",
            AuthEndpointHarness.ValidPassword,
            groupId: group.Id);
        AuthEndpointHarness.RenameGroup(Factory, group, "ИК-Ме (новое)");
        using var client = AuthEndpointHarness.CreateSessionClient(Factory, user.Id, UserRoles.Student);

        // when: GET /auth/me с access студента.
        using var response = await client.GetAsync(AuthEndpointHarness.MeEndpoint);

        // then: 200 MeDto; groupName по ТЕКУЩЕМУ состоянию групп.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await AuthEndpointHarness.ReadJsonAsync(response);
        Assert.Equal("me-student", body.RootElement.GetProperty("login").GetString());
        Assert.Equal("Тест Тестович Тестов", body.RootElement.GetProperty("fullName").GetString());
        Assert.Equal(UserRoles.Student, body.RootElement.GetProperty("role").GetString());
        Assert.Equal("ИК-Ме (новое)", body.RootElement.GetProperty("groupName").GetString());
    }

    [Fact]
    public async Task Me_Teacher_ReturnsNullGroupName()
    {
        // given: аутентифицированный teacher.
        var teacher = AuthEndpointHarness.SeedUser(
            Factory,
            "me-teacher",
            AuthEndpointHarness.ValidPassword,
            role: UserRoles.Teacher);
        using var client = AuthEndpointHarness.CreateSessionClient(Factory, teacher.Id, UserRoles.Teacher);

        // when: GET /auth/me.
        using var response = await client.GetAsync(AuthEndpointHarness.MeEndpoint);

        // then: 200; groupName = null.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await AuthEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("groupName").ValueKind);
    }

    [Fact]
    public async Task Me_StudentWithoutGroup_ReturnsNullGroupName()
    {
        // given: студент с groupId=null — второе из трёх состояний группы
        // (T-007/FR-011: студент с группой, студент без группы, teacher).
        var user = AuthEndpointHarness.SeedUser(
            Factory,
            "me-no-group",
            AuthEndpointHarness.ValidPassword,
            groupId: null);
        using var client = AuthEndpointHarness.CreateSessionClient(Factory, user.Id, UserRoles.Student);

        // when: GET /auth/me.
        using var response = await client.GetAsync(AuthEndpointHarness.MeEndpoint);

        // then: 200 MeDto; groupName = null.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await AuthEndpointHarness.ReadJsonAsync(response);
        Assert.Equal("me-no-group", body.RootElement.GetProperty("login").GetString());
        Assert.Equal(UserRoles.Student, body.RootElement.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("groupName").ValueKind);
    }

    [Fact]
    public async Task Me_StudentWithDanglingGroupId_ReturnsNullGroupName()
    {
        // given: groupId указывает на несуществующую группу — имя вычисляется по
        // ТЕКУЩЕМУ состоянию групп (FR-011), исчезнувшая группа даёт null.
        var user = AuthEndpointHarness.SeedUser(
            Factory,
            "me-dangling",
            AuthEndpointHarness.ValidPassword,
            groupId: Guid.NewGuid());
        using var client = AuthEndpointHarness.CreateSessionClient(Factory, user.Id, UserRoles.Student);

        // when: GET /auth/me.
        using var response = await client.GetAsync(AuthEndpointHarness.MeEndpoint);

        // then: 200 MeDto; groupName = null.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await AuthEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("groupName").ValueKind);
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsDeterministicUnauthorized()
    {
        // given: запрос без cookie access_token.
        using var client = AuthEndpointHarness.CreateClient(Factory);

        // when: GET /auth/me.
        using var response = await client.GetAsync(AuthEndpointHarness.MeEndpoint);

        // then: 401 «Не авторизован» (FR-022/IF-001).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Не авторизован", await AuthEndpointHarness.MessageAsync(response));
    }
}

/// <summary>
/// Юнит-ветка 409-фолбэка регистрации (IF-015 CONFLICT): гонка двух регистраций
/// на один login/email не воспроизводима через публичный API детерминированно —
/// pre-checks GetByLogin/GetByEmail её перекрывают; атомарная вставка репозитория
/// сигнализирует конфликт исключением StorageConflictException, которое контроллер
/// обязан перевести в 409 конверта с текстом ПО ПЕРЕЗАГРУЗКЕ (GetByLogin находит
/// победителя → «…с таким логином…»). Стаб репозитория — контролируемая заглушка
/// гонки, контроллер вызывается напрямую (unit-уровень, не эндпойнт; зеркало
/// GroupsUpdateRaceConflictUnitTests; HTTP-уровень гонки закрыт контуром B-03
/// Ts038_RegisterLoginRaceTests).
/// </summary>
public sealed class AuthRegisterRaceConflictUnitTests
{
    /// <summary>Декоратор: только Add сигнализирует конфликт гонки, остальное — внутренний склад.</summary>
    private sealed class RaceConflictingUserRepository(IUserRepository inner) : IUserRepository
    {
        public void Add(User user)
        {
            // Зеркало выигравшей параллельной регистрации: победитель уже в
            // складе, когда проигравший доходит до атомарной вставки.
            inner.Add(TestEntities.User("race-login"));
            throw new StorageConflictException("Гонка двух регистраций на один login (стаб).");
        }

        public User? GetById(Guid id) => inner.GetById(id);

        public User? GetByLogin(string login) => inner.GetByLogin(login);

        public User? GetByEmail(string email) => inner.GetByEmail(email);

        public void Update(User user) => inner.Update(user);

        public bool SetPassword(Guid userId, string passwordHash) =>
            inner.SetPassword(userId, passwordHash);

        public UpdateProfileResult UpdateProfile(Guid userId, string fullName, string email) =>
            inner.UpdateProfile(userId, fullName, email);

        public SetGroupResult SetGroup(Guid userId, Guid? groupId, Func<Guid, bool>? groupExists) =>
            inner.SetGroup(userId, groupId, groupExists);

        public IReadOnlyList<User> ListStudents(string? search = null, string? groupIdFilter = null) =>
            inner.ListStudents(search, groupIdFilter);

        public IReadOnlyList<User> ListByGroup(Guid groupId) => inner.ListByGroup(groupId);

        public int CountByGroup(Guid groupId) => inner.CountByGroup(groupId);
    }

    /// <summary>Минимальный IHostEnvironment (Development — cookie без Secure, зеркало CookieServiceTests).</summary>
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "LabsApp.Tests";

        public string EnvironmentName { get; set; } = "Development";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task Register_RepositoryRaceConflict_Returns409LoginConflict()
    {
        // given: контроллер с репозиторием, чья атомарная вставка сигнализирует гонку.
        var storage = TestStorage.Create();
        var options = new AuthOptions
        {
            Pbkdf2Iterations = 1000,
            JwtKey = "unit-test-jwt-key-0123456789abcdef0123456789",
        };
        using var meter = new Meter("labs.api.tests.auth-race", "1.0");
        using var counter = new KdfCounter(meter, storage.Time);
        var controller = new AuthController(
            new RaceConflictingUserRepository(storage.Users),
            storage.Groups,
            new Pbkdf2PasswordHasher(Options.Create(options), counter),
            new TokenService(Options.Create(options), storage.Time),
            new CookieService(new TestHostEnvironment()),
            new InMemorySecurityTokenRepository(storage.Lock, storage.Time),
            new SecurityEventLogger(NullLoggerFactory.Instance, new HttpContextAccessor()),
            new RegisterLimiter(storage.Time),
            new LoginFailureLimiter(storage.Time),
            new ClientIpResolver(),
            storage.Time);

        // Снимок ПОСЛЕ конструирования хэшера: эталонная деривация старта (метка
        // reference, IF-002) в дельту регистрации не входит.
        var kdfBefore = counter.Snapshot();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"fullName":"Гонка Регистраций","login":"race-login","email":"race-login@example.com","password":"Passw0rd!","repeatPassword":"Passw0rd!"}"""));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // when: POST /auth/register, вставка проигрывает гонку.
        var result = await controller.Register(CancellationToken.None);

        // then: 409 «Пользователь с таким логином уже существует» (победитель —
        // login); хэширование ДО вставки выполнено — ровно одна деривация (register).
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var envelope = Assert.IsType<ErrorEnvelope>(conflict.Value);
        Assert.Equal("Пользователь с таким логином уже существует", envelope.Message);
        var kdfAfter = counter.Snapshot();
        Assert.Equal(1, kdfAfter.Values.Sum() - kdfBefore.Values.Sum());
        Assert.Equal(1, (kdfAfter.TryGetValue(KdfCallers.Register, out var registerValue) ? registerValue : 0)
            - (kdfBefore.TryGetValue(KdfCallers.Register, out var beforeValue) ? beforeValue : 0));
    }
}
