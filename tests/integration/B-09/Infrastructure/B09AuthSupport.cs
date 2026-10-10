using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;

namespace LabsApp.IntegrationTests.B09.Infrastructure;

/// <summary>
/// Вспомогательные чтения кейсов батча TS-048..TS-052/TS-151..TS-153/TS-157
/// (вход/refresh/logout/recovery): эндпойнты IF-007/IF-013, дословные тексты
/// ошибок контракта, категории журнала (NFR-006), разбор экземпляров Set-Cookie
/// (NFR-007 — матрица по-экземплярно), декодирование JWT-payload без
/// верификации подписи (TS-049), SHA-256 hex (TS-050/TS-151), извлечение кода
/// восстановления из записей «EmailDev» (TS-151). Копия механики B08AuthHost
/// (чужие зоны недоступны для ссылок — BL-001 BUG-001).
/// </summary>
public static class B09AuthSupport
{
    // Контракты IF-007/IF-013 (эндпойнты кейсов батча).
    public const string RegisterEndpoint = "/api/v1/auth/register";
    public const string LoginEndpoint = "/api/v1/auth/login";
    public const string RefreshEndpoint = "/api/v1/auth/refresh";
    public const string LogoutEndpoint = "/api/v1/auth/logout";
    public const string MePasswordEndpoint = "/api/v1/me/password";
    public const string RecoveryRequestEndpoint = "/api/v1/auth/recovery/request";
    public const string RecoveryConfirmEndpoint = "/api/v1/auth/recovery/confirm";
    public const string ResetPasswordEndpoint = "/api/v1/auth/reset-password";

    /// <summary>Тексты ошибок контракта (дословно, IF-001).</summary>
    public const string WrongCredentialsMessage = "Неверный логин или пароль";
    public const string RateLimitedMessage = "Слишком много попыток. Повторите позже";

    /// <summary>Категория dev-писем (NFR-006) и маркер записи (ISS-003/SEC-002).</summary>
    public const string EmailDevCategory = "EmailDev";
    public const string EmailDevMarker = "[DEV-EMAIL]";

    /// <summary>Категория warnings конфигурации хостинга (TS-052).</summary>
    public const string HostingConfigurationCategory = "Hosting.Configuration";

    /// <summary>
    /// Клиент без авто-редиректов и без cookie-контейнера для хостов семейства
    /// B09AuthWebAppFactory (перегрузка механики B09AuthHttp.Create: фабрики
    /// матрицы ключей не унаследованы от B09WebAppFactory; BL-001 BUG-001).
    /// Cookie переносятся вручную через B09AuthHttp.SetRequestCookies.
    /// </summary>
    public static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    // ------------------------------------------------------------------
    // Ответы: чтение тела и гейт статуса шага потока
    // ------------------------------------------------------------------

    /// <summary>Гейт статуса шага потока: несовпадение — отказ с указанием шага, статуса и тела.</summary>
    public static void AssertStatus(HttpResponseMessage response, HttpStatusCode expected, string step)
    {
        Assert.True(
            response.StatusCode == expected,
            $"{step}: ожидался {(int)expected}, получен {(int)response.StatusCode}; тело: " +
            $"«{response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}».");
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

    // ------------------------------------------------------------------
    // Set-Cookie: разбор экземпляров (NFR-007 — матрица по-экземплярно)
    // ------------------------------------------------------------------

    /// <summary>
    /// Разбирает ВСЕ экземпляры Set-Cookie ответа (каждый заголовок — один
    /// экземпляр; NFR-007/ISS-010: матрица проверяется по-экземплярно).
    /// </summary>
    public static IReadOnlyList<B09AuthSetCookie> ParseSetCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return [];
        }

        return values.Select(ParseSetCookieValue).ToList();
    }

    /// <summary>Разбирает одну строку Set-Cookie: «name=value; attr; attr=value».</summary>
    public static B09AuthSetCookie ParseSetCookieValue(string raw)
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

        return new B09AuthSetCookie(name, value, attributes);
    }

    /// <summary>Единственная cookie с именем <paramref name="name"/>; иначе — отказ сценария.</summary>
    public static B09AuthSetCookie SingleCookie(HttpResponseMessage response, string name, string step)
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

    /// <summary>SHA-256 (hex, строчные) строки — зеркальная механика IF-003 (TS-050/TS-151).</summary>
    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    // ------------------------------------------------------------------
    // Журнал: извлечение кода восстановления из 'EmailDev' (TS-151)
    // ------------------------------------------------------------------

    /// <summary>Все 6-цифровые последовательности в записях (кандидаты кода восстановления).</summary>
    public static IReadOnlyList<string> ExtractSixDigitCodes(IEnumerable<B09LogRecord> records) =>
        records.Select(record => record.Serialize())
            .SelectMany(text => Regex.Matches(text, @"\b\d{6}\b").Select(match => match.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}

/// <summary>
/// Разобранный экземпляр Set-Cookie: имя, значение и словарь атрибутов
/// (флаги — с null-значением). Сравнение имён атрибутов — без учёта регистра.
/// </summary>
public sealed record B09AuthSetCookie(string Name, string Value, IReadOnlyDictionary<string, string?> Attributes)
{
    /// <summary>Присутствует ли флаг-атрибут без значения (HttpOnly, Secure).</summary>
    public bool HasFlag(string flag) => Attributes.ContainsKey(flag);

    /// <summary>Значение атрибута либо null (флаг без значения или отсутствие).</summary>
    public string? Attribute(string name) => Attributes.TryGetValue(name, out var value) ? value : null;
}
