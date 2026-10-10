using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B21.Infrastructure;

/// <summary>
/// Разобранный экземпляр Set-Cookie: имя, значение и словарь атрибутов
/// (флаги HttpOnly/Secure — с null-значением). Сравнение имён атрибутов —
/// без учёта регистра. Копия механики B08AuthSetCookie зоны B-08 (чужая зона
/// недоступна для ссылок, изоляция зон — BL-001 BUG-001); нужна кейсам
/// TS-194/TS-195 (NFR-007): матрица инспекций проверяется ПО-ЭКЗЕМПЛЯРНО
/// (каждый заголовок Set-Cookie — один экземпляр).
/// </summary>
public sealed record B21SetCookie(string Name, string Value, IReadOnlyDictionary<string, string?> Attributes)
{
    /// <summary>Присутствует ли флаг-атрибут без значения (HttpOnly, Secure).</summary>
    public bool HasFlag(string flag) => Attributes.ContainsKey(flag);

    /// <summary>Значение атрибута либо null (флаг без значения или отсутствие).</summary>
    public string? Attribute(string name) => Attributes.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// Механика auth-потока кейсов TS-194/TS-195 (NFR-007, матрица Set-Cookie
/// 7 экземпляров на 4 эндпойнтах) батча B-21: разбор всех экземпляров Set-Cookie
/// ответа, ручной cookie-контейнер клиента и общая последовательность
/// register → login → refresh (по refresh-cookie от login) → logout (по свежей
/// сессии) — дословно given кейсов. Копия механики B08AuthHost зоны B-08
/// (чужая зона недоступна для ссылок, изоляция зон — BL-001 BUG-001).
/// </summary>
public static class B21CookieFlowHost
{
    // Контракты IF-007 (эндпойнты auth-потока кейсов).
    public const string RegisterEndpoint = "/api/v1/auth/register";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RefreshEndpoint = "/api/v1/auth/refresh";
    public const string LogoutEndpoint = "/api/v1/auth/logout";

    /// <summary>
    /// Разбирает ВСЕ экземпляры Set-Cookie ответа (каждый заголовок — один
    /// экземпляр; NFR-007/ISS-010: матрица проверяется по-экземплярно).
    /// </summary>
    public static IReadOnlyList<B21SetCookie> ParseSetCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return [];
        }

        return values.Select(ParseSetCookieValue).ToList();
    }

    /// <summary>Разбирает одну строку Set-Cookie: «name=value; attr; attr=value».</summary>
    public static B21SetCookie ParseSetCookieValue(string raw)
    {
        var segments = raw.Split(';');
        var nameValue = segments[0].Trim();
        var separator = nameValue.IndexOf('=');
        var name = separator < 0 ? nameValue : nameValue[..separator].Trim();
        var value = separator < 0 ? string.Empty : nameValue[(separator + 1)..].Trim();

        var attributes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < segments.Length; index++)
        {
            var segment = segments[index].Trim();
            if (segment.Length == 0)
            {
                continue;
            }

            var equals = segment.IndexOf('=');
            if (equals < 0)
            {
                attributes[segment] = null;
            }
            else
            {
                attributes[segment[..equals].Trim()] = segment[(equals + 1)..].Trim();
            }
        }

        return new B21SetCookie(name, value, attributes);
    }

    /// <summary>
    /// Гейт статуса шага auth-потока: несовпадение — отказ с указанием шага,
    /// статуса и тела (диагностика для стадий run/BUG-раунда).
    /// </summary>
    public static async Task AssertStatusAsync(
        HttpResponseMessage response, HttpStatusCode expected, string step)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new Xunit.Sdk.XunitException(
            $"{step}: ожидался {(int)expected}, получен {(int)response.StatusCode}; тело: «{body}».");
    }

    /// <summary>
    /// given/when кейсов TS-194/TS-195: последовательность register → login →
    /// refresh (по refresh-cookie от login) → logout (по свежей сессии) на одном
    /// клиенте с ручным cookie-контейнером; возвращает экземпляры Set-Cookie
    /// каждого ответа. Ровно 7 экземпляров суммарно (2+2+1+2).
    /// </summary>
    public static async Task<(
        IReadOnlyList<B21SetCookie> Register,
        IReadOnlyList<B21SetCookie> Login,
        IReadOnlyList<B21SetCookie> Refresh,
        IReadOnlyList<B21SetCookie> Logout)> RunFourEndpointFlowAsync(
            WebApplicationFactory<Program> factory,
            string loginName,
            string email,
            string fullName,
            string password)
    {
        using var client = new B21CookieFlowClient(factory);

        // (1) register → 201, ровно 2 Set-Cookie (access_token + refresh_token).
        using (var register = await client.PostAsync(RegisterEndpoint, new
        {
            fullName,
            login = loginName,
            email,
            password,
            repeatPassword = password,
        }))
        {
            await AssertStatusAsync(register, HttpStatusCode.Created, "register (TS-194/TS-195)");
            var registerCookies = ParseSetCookie(register);

            // (2) login → 200, ровно 2 Set-Cookie (контейнер переносит cookie
            // регистрации; тело ответа решает вход).
            using (var login = await client.PostAsync(LoginEndpoint, new
            {
                login = loginName,
                password,
            }))
            {
                await AssertStatusAsync(login, HttpStatusCode.OK, "login (TS-194/TS-195)");
                var loginCookies = ParseSetCookie(login);

                // (3) refresh → 204 по refresh-cookie от login (в контейнере),
                // ровно 1 Set-Cookie (refresh_token не переустанавливается).
                using (var refresh = await client.PostAsync(RefreshEndpoint, null))
                {
                    await AssertStatusAsync(refresh, HttpStatusCode.NoContent, "refresh (TS-194/TS-195)");
                    var refreshCookies = ParseSetCookie(refresh);

                    // (4) logout → 204 по свежей сессии (обновлённый access в
                    // контейнере), ровно 2 сброса Max-Age=0.
                    using (var logout = await client.PostAsync(LogoutEndpoint, null))
                    {
                        await AssertStatusAsync(logout, HttpStatusCode.NoContent, "logout (TS-194/TS-195)");
                        var logoutCookies = ParseSetCookie(logout);

                        return (registerCookies, loginCookies, refreshCookies, logoutCookies);
                    }
                }
            }
        }
    }

    /// <summary>
    /// then кейсов TS-194/TS-195 (NFR-007): поэкземплярная матрица всех 7
    /// Set-Cookie — состав «имя → Max-Age» по эндпойнтам (register — access 900
    /// и refresh 604800; login — 900 и 604800; refresh — ТОЛЬКО access 900,
    /// refresh_token не переустанавливается; logout — оба сброса с Max-Age=0);
    /// HttpOnly, SameSite=Strict, Path=/ — 7 из 7; Secure — 7/7 при
    /// expectSecure=true (Production) и 0/7 при false (Development).
    /// </summary>
    public static void AssertSetCookieMatrix(
        IReadOnlyList<B21SetCookie> register,
        IReadOnlyList<B21SetCookie> login,
        IReadOnlyList<B21SetCookie> refresh,
        IReadOnlyList<B21SetCookie> logout,
        bool expectSecure)
    {
        var label = expectSecure ? "Production" : "Development";

        // then: всего 7 экземпляров: register (2), login (2), refresh (1), logout (2).
        Assert.Equal(2, register.Count);
        Assert.Equal(2, login.Count);
        Assert.Single(refresh);
        Assert.Equal(2, logout.Count);

        // then: состав пар «имя → Max-Age» по экземплярам (порядок экземпляров
        // в ответе спекой не фиксируется — сравнение по упорядоченному имени).
        Assert.Equal(
            new (string, string?)[] { ("access_token", "900"), ("refresh_token", "604800") },
            OrderByName(register).Select(cookie => (cookie.Name, cookie.Attribute("max-age"))).ToArray());
        Assert.Equal(
            new (string, string?)[] { ("access_token", "900"), ("refresh_token", "604800") },
            OrderByName(login).Select(cookie => (cookie.Name, cookie.Attribute("max-age"))).ToArray());
        Assert.Equal(
            new (string, string?)[] { ("access_token", "900") },
            OrderByName(refresh).Select(cookie => (cookie.Name, cookie.Attribute("max-age"))).ToArray());
        Assert.Equal(
            new (string, string?)[] { ("access_token", "0"), ("refresh_token", "0") },
            OrderByName(logout).Select(cookie => (cookie.Name, cookie.Attribute("max-age"))).ToArray());

        // then: HttpOnly, SameSite=Strict, Path=/ — 7 из 7.
        var all = register.Concat(login).Concat(refresh).Concat(logout).ToList();
        Assert.Equal(7, all.Count);
        foreach (var cookie in all)
        {
            Assert.True(cookie.HasFlag("httponly"), $"[{label}] {cookie.Name}: нет атрибута HttpOnly.");
            Assert.Equal("strict", cookie.Attribute("samesite"));
            Assert.Equal("/", cookie.Attribute("path"));
        }

        // then: Secure — 7/7 в Production и 0/7 в Development (NFR-007/FR-008).
        var secureCount = all.Count(cookie => cookie.HasFlag("secure"));
        Assert.True(
            secureCount == (expectSecure ? 7 : 0),
            $"[{label}]: флаг Secure должен присутствовать на {all.Count} из {all.Count} "
            + $"экземпляров (Production) или ни на одном (Development), фактически — "
            + $"на {secureCount} из {all.Count} "
            + $"[{string.Join(", ", all.Select(cookie => cookie.Name))}].");
    }

    private static IReadOnlyList<B21SetCookie> OrderByName(IReadOnlyList<B21SetCookie> cookies) =>
        cookies.OrderBy(cookie => cookie.Name, StringComparer.Ordinal).ToArray();
}

/// <summary>
/// Клиент auth-потока кейсов TS-194/TS-195: без авто-редиректов (точные
/// статусы) и БЕЗ автоматического CookieContainer — состав и атрибуты cookie
/// являются предметом кейсов, поэтому cookie переносит ручной
/// <see cref="B21CookieJar"/>, а каждый тест разбирает Set-Cookie по сырому
/// ответу (<see cref="B21CookieFlowHost.ParseSetCookie"/>).
/// </summary>
public sealed class B21CookieFlowClient : IDisposable
{
    private readonly HttpClient _http;

    public B21CookieFlowClient(WebApplicationFactory<Program> factory)
    {
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }

    /// <summary>Ручной cookie-контейнер клиента (единственный источник cookie запросов).</summary>
    public B21CookieJar Cookies { get; } = new();

    /// <summary>Отправляет запрос: контейнер дополняет Cookie-заголовок, ответ — источник Set-Cookie.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        Cookies.ApplyTo(request);
        var response = await _http.SendAsync(request);
        Cookies.CaptureFrom(response);
        return response;
    }

    /// <summary>POST с JSON-телом (null — без тела, refresh/logout).</summary>
    public Task<HttpResponseMessage> PostAsync(string url, object? jsonBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (jsonBody is not null)
        {
            request.Content = JsonContent.Create(jsonBody);
        }

        return SendAsync(request);
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// Ручной cookie-контейнер клиента кейсов TS-194/TS-195: хранит пары
/// «имя=значение» из Set-Cookie ответов (Max-Age=0 — удаление cookie, семантика
/// logout) и переносит их в Cookie-заголовок последующих запросов. Атрибуты
/// cookie контейнером НЕ интерпретируются (кроме Max-Age=0) — их проверяют
/// тесты по сырому ответу (NFR-007). Копия механики B08AuthCookieJar (чужая
/// зона недоступна для ссылок, BL-001 BUG-001).
/// </summary>
public sealed class B21CookieJar
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    /// <summary>Текущие пары «имя → значение» (копия на момент вызова).</summary>
    public IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, string>(_cookies, StringComparer.Ordinal);
        }
    }

    /// <summary>Явная установка значения cookie.</summary>
    public void Set(string name, string value)
    {
        lock (_gate)
        {
            _cookies[name] = value;
        }
    }

    /// <summary>Дополняет запрос Cookie-заголовком из текущих cookie.</summary>
    public void ApplyTo(HttpRequestMessage request)
    {
        lock (_gate)
        {
            if (_cookies.Count == 0)
            {
                return;
            }

            request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(pair => $"{pair.Key}={pair.Value}")));
        }
    }

    /// <summary>Поглощает Set-Cookie ответа: Max-Age=0 — удаление, иначе — установка.</summary>
    public void CaptureFrom(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        foreach (var raw in values)
        {
            var cookie = B21CookieFlowHost.ParseSetCookieValue(raw);
            var maxAge = cookie.Attribute("max-age");
            lock (_gate)
            {
                if (maxAge == "0")
                {
                    _cookies.Remove(cookie.Name);
                }
                else
                {
                    _cookies[cookie.Name] = cookie.Value;
                }
            }
        }
    }
}

/// <summary>
/// Тестовый хост матрицы Set-Cookie (кейсы TS-194/TS-195, NFR-007): собственная
/// копия механики фабрики зоны B-21 (фабрики чужих зон — internal; изоляция
/// зон — BL-001 BUG-001) с ПАРАМЕТРОМ окружения: Development — TS-194,
/// Production — TS-195 (given: «заданы Auth__JwtKey и нестандартный
/// Seed__TeacherPassword»; guard'ы FR-006/FR-025 требуют в Production ключ
/// подписи ≥32 символа и пароль сида, отличный от умолчания teacher123!).
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения). Seed__DemoData=false ЯВНО — кейсы не зависят от
/// демо-набора (AR-011/NFR-011); Auth__Pbkdf2Iterations=1000 — auth-поток
/// кейсов выполняет деривации на тестовой поверхности KDF (FR-005).
/// </summary>
public class B21CookieMatrixFactory : WebApplicationFactory<Program>
{
    /// <summary>Ключ подписи JWT dev-фикстуры (≥32 символа).</summary>
    public const string DevJwtKey = "b21-cookie-matrix-dev-jwt-signing-key-0123456789abcdef";

    /// <summary>Пароль сид-преподавателя dev-фикстуры (не умолчание).</summary>
    public const string DevTeacherPassword = "b21-cookie-dev-teacher-password1!";

    /// <summary>Ключ подписи JWT Production-фикстуры (≥32 символа; given TS-195 — «задан»).</summary>
    public const string ProductionJwtKey = "b21-cookie-matrix-production-jwt-signing-key-0123456789";

    /// <summary>Пароль сид-преподавателя Production-фикстуры — НЕстандартный (given TS-195).</summary>
    public const string ProductionTeacherPassword = "b21-cookie-prod-teacher-password1!";

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>Внутренний конструктор — окружение и набор настроек фикстуры.</summary>
    protected B21CookieMatrixFactory(string environment, IReadOnlyDictionary<string, string?> settings)
    {
        _environment = environment;
        _settings = settings;
    }

    /// <summary>Development-фикстура с ЯВНЫМИ Auth__JwtKey и Seed__TeacherPassword (TS-194).</summary>
    public static B21CookieMatrixFactory CreateDevelopment() => new(
        Environments.Development,
        new Dictionary<string, string?>
        {
            [AuthOptions.JwtKeyVariable] = DevJwtKey,
            [SeedOptions.TeacherPasswordVariable] = DevTeacherPassword,
        });

    /// <summary>Production-фикстура с валидными секретами (TS-195).</summary>
    public static B21CookieMatrixFactory CreateProduction() => new(
        Environments.Production,
        new Dictionary<string, string?>
        {
            [AuthOptions.JwtKeyVariable] = ProductionJwtKey,
            [SeedOptions.TeacherPasswordVariable] = ProductionTeacherPassword,
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.Pbkdf2IterationsVariable), "1000");
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(ToConfigKey(key), value);
        }
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
