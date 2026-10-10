using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B17.Infrastructure;

/// <summary>
/// Клиент, предусловия и общие проверки кейсов батча B-17 (восстановление
/// пароля v2.2: TS-068..TS-072, TS-190). Контракты эндпоинтов — FR-012/FR-013;
/// тексты ошибок — FR-004/FR-013 дословно; категория dev-писем и маркер —
/// ISS-003/SEC-002 (категория 'EmailDev', маркер [DEV-EMAIL]).
/// </summary>
public static partial class B17Host
{
    // Контракты FR-012/FR-013 (эндпоинты кейсов батча).
    public const string RecoveryRequestEndpoint = "/api/v1/auth/recovery/request";
    public const string RecoveryConfirmEndpoint = "/api/v1/auth/recovery/confirm";

    /// <summary>Пароль всех сид-пользователей батча (правилам §8 удовлетворяет; сид идёт напрямую в хэш).</summary>
    public const string TestUserPassword = "Passw0rd!";

    /// <summary>Тексты ошибок контракта (дословно, FR-004/FR-013).</summary>
    public const string RateLimitedMessage = "Слишком много попыток. Повторите позже";
    public const string CodeRejectedMessage = "Код восстановления не подходит";

    /// <summary>Категория dev-писем и маркер записи (ISS-003/SEC-002, IF-005 v2.2 — дословно).</summary>
    public const string DevEmailCategory = "EmailDev";
    public const string DevEmailMarker = "[DEV-EMAIL]";

    /// <summary>Клиент батча: recovery-эндпоинты анонимны, cookie составом кейсов не управляются.</summary>
    public static HttpClient CreateClient(B17WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>POST с JSON-телом (сырая строка — кейсы требуют точных тел).</summary>
    public static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return await client.SendAsync(request);
    }

    /// <summary>DI-сид пользователя с реальным PBKDF2-хэшем пароля (ADR-015).</summary>
    public static User SeedUser(
        B17WebAppFactory factory,
        string login,
        string email,
        string fullName,
        string role)
    {
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        var clock = factory.Services.GetRequiredService<TimeProvider>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = hasher.Hash(TestUserPassword, KdfCallers.Seed),
            FullName = fullName,
            Role = role,
            GroupId = null,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        users.Add(user);
        return user;
    }

    /// <summary>DI-сид студента (роль student).</summary>
    public static User SeedStudent(B17WebAppFactory factory, string login, string email, string fullName) =>
        SeedUser(factory, login, email, fullName, UserRoles.Student);

    /// <summary>
    /// Полное число записей кодов восстановления в хранилище тестового хоста
    /// (then «создан ровно один живой код» / «в хранилище не появилось кодов»):
    /// интерфейс FR-024 перечисления не даёт, инспекция — по внутреннему словарю
    /// консолидированной in-memory реализации ISecurityTokenRepository (IF-015/
    /// T-101: recovery-коды живут в едином токенном хранилище; копии-снимки сущностей).
    /// </summary>
    public static int CountRecoveryCodeRecords(B17WebAppFactory factory) =>
        ReadRecoveryCodeTable(factory).Count;

    /// <summary>
    /// Снимок ВСЕХ записей кодов восстановления хранилища (then «C1.usedAt≠null;
    /// новый код C2 жив» — сопоставление конкретных записей по Id).
    /// </summary>
    public static IReadOnlyList<RecoveryCode> GetAllRecoveryCodeRecords(B17WebAppFactory factory) =>
        ReadRecoveryCodeTable(factory).Values
            .Select(code => new RecoveryCode
            {
                Id = code.Id,
                UserId = code.UserId,
                CodeHash = code.CodeHash,
                ExpiresAt = code.ExpiresAt,
                UsedAt = code.UsedAt,
                Attempts = code.Attempts,
                CreatedAt = code.CreatedAt,
            })
            .ToList();

    /// <summary>
    /// Внутренний словарь recovery-кодов in-memory реализации
    /// ISecurityTokenRepository (поле _recoveryCodes, IF-015: DI-регистрация —
    /// InMemorySecurityTokenRepository).
    /// </summary>
    private static Dictionary<Guid, RecoveryCode> ReadRecoveryCodeTable(B17WebAppFactory factory)
    {
        var repository = factory.Services.GetRequiredService<ISecurityTokenRepository>();
        var field = typeof(InMemorySecurityTokenRepository).GetField(
            "_recoveryCodes",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new Xunit.Sdk.XunitException(
                "Инспекция хранилища кодов: поле _recoveryCodes не найдено в InMemorySecurityTokenRepository.");
        return (Dictionary<Guid, RecoveryCode>)field.GetValue(repository)!;
    }

    /// <summary>
    /// Тело ответа — 0 байт и Content-Length: 0 (ISS-014: НЕ JSON-объект '{}',
    /// FR-012 (5): «ответ 200 с ПУСТЫМ телом (0 байт, Content-Length: 0)»).
    /// </summary>
    public static async Task AssertEmptyBodyWithZeroContentLengthAsync(HttpResponseMessage response, string context)
    {
        var content = await response.Content.ReadAsStringAsync();
        var contentLength = response.Content.Headers.ContentLength;

        Assert.True(
            content.Length == 0,
            $"{context}: ожидалось тело 0 байт (ISS-014: НЕ JSON-объект), фактически {content.Length} байт: «{content}».");
        Assert.True(
            contentLength == 0,
            $"{context}: ожидался заголовок Content-Length: 0, фактически "
            + $"{contentLength?.ToString(CultureInfo.InvariantCulture) ?? "<отсутствует>"}.");
    }

    /// <summary>Значение ключа message единого конверта ошибок (IF-001) — дословно.</summary>
    public static string ExtractEnvelopeMessage(string content, string context)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            Assert.True(
                root.ValueKind == JsonValueKind.Object,
                $"{context}: ожидался JSON-объект конверта, фактически ValueKind={root.ValueKind}: «{content}».");
            Assert.True(
                root.TryGetProperty("message", out var message),
                $"{context}: в теле отсутствует ключ message: «{content}».");
            Assert.True(
                message.ValueKind == JsonValueKind.String,
                $"{context}: ключ message не строка: «{content}».");
            return message.GetString() ?? string.Empty;
        }
        catch (JsonException exception)
        {
            throw new Xunit.Sdk.XunitException(
                $"{context}: тело ответа не является JSON: «{content}» ({exception.Message}).");
        }
    }

    /// <summary>Строка из ровно 6 ASCII-цифр в сериализованной записи; null — совпадений нет.</summary>
    public static string? ExtractSixDigitCode(B17LogRecord record)
    {
        var match = SixAsciiDigits().Match(record.Serialize());
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Запись содержит ИМЕННО этот код (сопоставление по точному значению с
    /// границами по цифрам — чтобы не считать частью кода цифры соседних полей
    /// вида durationMs=1234567 или hex-traceId).
    /// </summary>
    public static bool ContainsCode(B17LogRecord record, string code) =>
        Regex.IsMatch(record.Serialize(), $"(?<![0-9]){Regex.Escape(code)}(?![0-9])", RegexOptions.CultureInvariant);

    [GeneratedRegex("(?<![0-9])[0-9]{6}(?![0-9])")]
    private static partial Regex SixAsciiDigits();
}
