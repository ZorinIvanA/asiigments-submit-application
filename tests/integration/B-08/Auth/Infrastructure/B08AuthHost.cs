using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Auth.Infrastructure;

/// <summary>
/// Клиент и предусловия тестовых хостов поддерева Auth/ зоны B-08.
///
/// Клиент — без авто-редиректов (точные статусы) и БЕЗ автоматического
/// CookieContainer: состав и форма cookie — предмет кейсов TS-048, TS-050,
/// TS-151, TS-152, поэтому cookie переносит ручной
/// <see cref="B08AuthCookieJar"/> (контейнер только хранит/переносит значения;
/// утверждения по Set-Cookie каждый тест делает по сырому ответу через
/// <see cref="ParseSetCookie"/>).
///
/// Пользователи создаются DI-сидом через IUserRepository + IPasswordHasher
/// (ADR-015): хэш — реальным PBKDF2-хэшером с меткой seed, что даёт хэш
/// формата IF-002 («pbkdf2-sha256$…»), принимаемого строгим парсером Verify.
/// Сессии для кейсов TS-048..TS-050/TS-151/TS-152 создаются САМИМ POST
/// /auth/login (предмет кейса — форма ответа входа).
///
/// Δkdf-гейты (TS-027..TS-029): IKdfCounter не имеет сброса — «счётчик
/// обнулён/сброшен» кейсов исполняется базовой линией Snapshot() перед шагом
/// и разностью после (стандартная методика гейтов FR-027).
/// </summary>
public static class B08AuthHost
{
    // Контракты IF-007/IF-013 (эндпоинты кейсов батча).
    public const string RegisterEndpoint = "/api/v1/auth/register";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RefreshEndpoint = "/api/v1/auth/refresh";
    public const string LogoutEndpoint = "/api/v1/auth/logout";
    public const string MeEndpoint = "/api/v1/auth/me";
    public const string MePasswordEndpoint = "/api/v1/me/password";
    public const string RecoveryRequestEndpoint = "/api/v1/auth/recovery/request";
    public const string RecoveryConfirmEndpoint = "/api/v1/auth/recovery/confirm";
    public const string ResetPasswordEndpoint = "/api/v1/auth/reset-password";

    /// <summary>Пароль DI-сид-пользователей по умолчанию (правилам §8 удовлетворяет).</summary>
    public const string TestUserPassword = "Passw0rd!";

    /// <summary>
    /// Тексты ошибок контракта (дословно, словарь ErrorTexts хостинга/домена).
    /// </summary>
    public const string WrongCredentialsMessage = "Неверный логин или пароль";
    public const string RateLimitedMessage = "Слишком много попыток. Повторите позже";

    /// <summary>Категория dev-писем (NFR-006) и маркер записи (ISS-003/SEC-002).</summary>
    public const string EmailDevCategory = "EmailDev";
    public const string EmailDevMarker = "[DEV-EMAIL]";

    /// <summary>Категория warnings конфигурации хостинга (TS-052).</summary>
    public const string HostingConfigurationCategory = "Hosting.Configuration";

    /// <summary>Клиент поддерева: тест управляет составом cookie через ручной контейнер.</summary>
    public static B08AuthClient CreateClient(B08AuthWebAppFactory factory) => new(factory);

    /// <summary>DI-сид пользователя с реальным PBKDF2-хэшем пароля (IF-002, метка seed).</summary>
    public static User SeedUser(
        B08AuthWebAppFactory factory,
        string login,
        string email,
        string fullName,
        string role,
        string? password = null)
    {
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        var clock = factory.Services.GetRequiredService<TimeProvider>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = hasher.Hash(password ?? TestUserPassword, KdfCallers.Seed),
            FullName = fullName,
            Role = role,
            GroupId = null,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        users.Add(user);
        return user;
    }

    /// <summary>DI-сид преподавателя (роль teacher).</summary>
    public static User SeedTeacher(
        B08AuthWebAppFactory factory,
        string login,
        string email,
        string fullName,
        string? password = null) =>
        SeedUser(factory, login, email, fullName, UserRoles.Teacher, password);

    /// <summary>
    /// Разность снимков IKdfCounter (Δkdf): приращения по меткам вызывателя
    /// между базовой линией и текущим состоянием (гейты FR-027).
    /// </summary>
    public static IReadOnlyDictionary<string, long> KdfDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after)
    {
        var delta = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (caller, value) in after)
        {
            var increment = value - before.GetValueOrDefault(caller);
            if (increment != 0)
            {
                delta[caller] = increment;
            }
        }

        return delta;
    }

    /// <summary>Сумма значений словаря меток (гейт «сумма по всем меткам», TS-029).</summary>
    public static long Total(this IReadOnlyDictionary<string, long> values) => values.Values.Sum();

    // ------------------------------------------------------------------
    // Set-Cookie: разбор экземпляров (NFR-007 — матрица по-экземплярно)
    // ------------------------------------------------------------------

    /// <summary>
    /// Разбирает ВСЕ экземпляры Set-Cookie ответа (каждый заголовок — один
    /// экземпляр; NFR-007/ISS-010: матрица проверяется по-экземплярно).
    /// </summary>
    public static IReadOnlyList<B08AuthSetCookie> ParseSetCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return [];
        }

        return values.Select(ParseSetCookieValue).ToList();
    }

    /// <summary>Разбирает одну строку Set-Cookie: «name=value; attr; attr=value».</summary>
    public static B08AuthSetCookie ParseSetCookieValue(string raw)
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

        return new B08AuthSetCookie(name, value, attributes);
    }

    /// <summary>Единственная cookie с именем <paramref name="name"/>; иначе — отказ сценария.</summary>
    public static B08AuthSetCookie SingleCookie(HttpResponseMessage response, string name, string step)
    {
        var cookies = ParseSetCookie(response);
        var matches = cookies.Where(cookie => string.Equals(cookie.Name, name, StringComparison.Ordinal)).ToList();
        Assert.True(
            matches.Count == 1,
            $"{step}: ожидался ровно один Set-Cookie '{name}', получено {matches.Count} " +
            $"(все экземпляры: [{string.Join(", ", cookies.Select(cookie => cookie.Name))}]).");
        return matches[0];
    }

    // ------------------------------------------------------------------
    // JWT: декодирование payload без верификации подписи (TS-049)
    // ------------------------------------------------------------------

    /// <summary>
    /// Декодирует payload access-JWT (средний сегмент base64url) БЕЗ верификации
    /// подписи — дословно when кейса TS-049.
    /// </summary>
    public static JsonElement DecodeJwtPayload(string token)
    {
        var parts = token.Split('.');
        Assert.True(parts.Length == 3, $"access-токен не JWT (сегментов: {parts.Length}).");
        var json = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(parts[1]));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>SHA-256 (hex, строчные) строки — зеркальная механика IF-003 (TS-050).</summary>
    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    // ------------------------------------------------------------------
    // Журнал: извлечение кода восстановления из 'EmailDev' (TS-151)
    // ------------------------------------------------------------------

    /// <summary>Все 6-цифровые последовательности в записях (кандидаты кода восстановления).</summary>
    public static IReadOnlyList<string> ExtractSixDigitCodes(IEnumerable<B08AuthLogRecord> records) =>
        records.Select(record => record.Serialize())
            .SelectMany(text => Regex.Matches(text, @"\b\d{6}\b").Select(match => match.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // ------------------------------------------------------------------
    // Ответы: чтение тела и гейт статуса шага потока
    // ------------------------------------------------------------------

    /// <summary>Читает тело ответа строкой (для сообщений об отказах шагов).</summary>
    public static async Task<string> ReadBodyAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception exception)
        {
            return $"<тело не прочитано: {exception.Message}>";
        }
    }

    /// <summary>
    /// Гейт статуса шага потока: несовпадение — отказ с указанием шага, статуса
    /// и тела (диагностика для стадий run/BUG-раунда).
    /// </summary>
    public static void AssertStatus(HttpResponseMessage response, HttpStatusCode expected, string step)
    {
        Assert.True(
            response.StatusCode == expected,
            $"{step}: ожидался {(int)expected}, получен {(int)response.StatusCode}; тело: «{response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}».");
    }

    /// <summary>Разбирает JSON-тело ответа; корень обязан быть объектом.</summary>
    public static async Task<JsonElement> ReadJsonObjectAsync(HttpResponseMessage response, string step)
    {
        var content = await response.Content.ReadAsStringAsync();
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            Assert.True(root.ValueKind == JsonValueKind.Object, $"{step}: корень тела не JSON-объект.");
            return root.Clone();
        }
        catch (JsonException exception)
        {
            throw new Xunit.Sdk.XunitException($"{step}: тело не является JSON: «{content}» ({exception.Message}).");
        }
    }

    /// <summary>Строковое свойство JSON-объекта (отсутствующее — null).</summary>
    public static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Разворачивает цепочку исключений в одну строку сообщений (диагностика TS-051).</summary>
    public static string FlattenExceptionMessages(Exception exception)
    {
        var builder = new StringBuilder();
        var current = (Exception?)exception;
        var depth = 0;
        while (current is not null && depth < 16)
        {
            builder.Append(current.Message).Append(' ');
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    builder.Append(FlattenExceptionMessages(inner)).Append(' ');
                }
            }

            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }
}

/// <summary>
/// Разобранный экземпляр Set-Cookie: имя, значение и словарь атрибутов
/// (флаги — с null-значением). Сравнение имён атрибутов — без учёта регистра.
/// </summary>
public sealed record B08AuthSetCookie(string Name, string Value, IReadOnlyDictionary<string, string?> Attributes)
{
    /// <summary>Присутствует ли флаг-атрибут без значения (HttpOnly, Secure).</summary>
    public bool HasFlag(string flag) => Attributes.ContainsKey(flag);

    /// <summary>Значение атрибута либо null (флаг без значения или отсутствие).</summary>
    public string? Attribute(string name) => Attributes.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// Клиент поддерева Auth/ (копия механики B08Host.Client корня зоны): ручной
/// cookie-контейнер — единственный источник cookie запросов; ответ — источник
/// Set-Cookie для инспекций кейсов. Плюс перегрузка с дополнительным заголовком
/// (TS-157: X-Forwarded-For) и PUT (TS-151: me/password).
/// </summary>
public sealed class B08AuthClient : IDisposable
{
    private readonly HttpClient _http;

    public B08AuthClient(B08AuthWebAppFactory factory)
    {
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }

    /// <summary>Ручной cookie-контейнер клиента.</summary>
    public B08AuthCookieJar Cookies { get; } = new();

    /// <summary>Отправляет запрос: контейнер дополняет Cookie-заголовок, ответ — источник Set-Cookie.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, (string Name, string Value)? header = null)
    {
        Cookies.ApplyTo(request);
        if (header is { } extra)
        {
            request.Headers.Add(extra.Name, extra.Value);
        }

        var response = await _http.SendAsync(request);
        Cookies.CaptureFrom(response);
        return response;
    }

    public Task<HttpResponseMessage> GetAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        return SendAsync(request);
    }

    /// <summary>POST с JSON-телом (сырая строка — кейсы требуют точных тел).</summary>
    public Task<HttpResponseMessage> PostAsync(string url, string? json, (string Name, string Value)? header = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return SendAsync(request, header);
    }

    /// <summary>PUT с JSON-телом (TS-151: PUT /me/password).</summary>
    public Task<HttpResponseMessage> PutAsync(string url, string? json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return SendAsync(request);
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// Ручной cookie-контейнер клиента: хранит пары «имя=значение» из Set-Cookie
/// ответов (Max-Age=0 — удаление cookie, семантика logout) и переносит их в
/// Cookie-заголовок последующих запросов. Атрибуты cookie контейнером НЕ
/// интерпретируются — их проверяют тесты по сырому ответу (NFR-007).
/// </summary>
public sealed class B08AuthCookieJar
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

    /// <summary>Значения всех текущих cookie (для набора секретов TS-151).</summary>
    public IReadOnlyList<string> Values()
    {
        lock (_gate)
        {
            return [.. _cookies.Values];
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
            var cookie = B08AuthHost.ParseSetCookieValue(raw);
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
