using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B22.Infrastructure;

namespace LabsApp.IntegrationTests.B22.Scenarios;

/// <summary>
/// TS-189 (NFR-006, P0): полный auth-поток не раскрывает секреты в основных
/// категориях лога (Development).
///
/// given: Development-стенд (B22WebAppFactory — Development, log-sink B22LogSink
///        собирает записи всех категорий); зафиксированы отправленные секреты:
///        пароль регистрации/сброса, код восстановления, resetToken, значения
///        cookie.
/// when:  полный поток: register → login → recovery/request → recovery/confirm →
///        reset-password → logout; затем проверка всех записей sink вне
///        категории 'EmailDev'.
/// then:  ни одна запись вне 'EmailDev' не содержит ни один из секретов; код
///        присутствует только в [DEV-EMAIL]-записи (NFR-006 verification (а),
///        ISS-003/SEC-002).
/// </summary>
public sealed class Ts189_SecretLeakageTests : IClassFixture<Ts189_SecretLeakageTests.Fixture>
{
    /// <summary>Выделенная категория dev-почты (кейс/SEC-002: дословно 'EmailDev').</summary>
    private const string DevEmailCategory = "EmailDev";

    /// <summary>Маркер dev-письма с кодом (кейс/SEC-002: дословно '[DEV-EMAIL]').</summary>
    private const string DevEmailMarker = "[DEV-EMAIL]";

    private const string RegisterPassword = "B189-Register-Password1!";

    private readonly Fixture _fixture;

    public Ts189_SecretLeakageTests(Fixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Изолированный хост кейса: только его auth-поток попадает в sink.</summary>
    public sealed class Fixture : IDisposable
    {
        public B22WebAppFactory Factory { get; } = new();

        public void Dispose() => Factory.Dispose();
    }

    [Fact]
    public async Task FullAuthFlow_NoRecordOutsideEmailDev_ContainsAnySecret_CodeOnlyInDevEmailRecords()
    {
        var factory = _fixture.Factory;
        using var client = B22Sessions.Create(factory);

        const string login = "b189-flow-user";
        const string email = "b189-flow-user@example.com";

        // when: полный поток register → login → recovery/request → recovery/confirm
        // → reset-password → logout; секреты фиксируются по ходу.
        var secrets = new List<(string Description, string Value)>();

        // --- register ---
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "B189 Поток Регистраций",
            login,
            email,
            password = RegisterPassword,
            repeatPassword = RegisterPassword,
        });
        Assert.True(
            (int)registerResponse.StatusCode is >= 200 and < 300,
            "Шаг given не исполним: POST /api/v1/auth/register не успешен "
            + $"({(int)registerResponse.StatusCode} {registerResponse.StatusCode}) — полный auth-поток "
            + $"NFR-006(а) прерван. Тело: {await registerResponse.Content.ReadAsStringAsync()}");
        CollectCookies(registerResponse, secrets);

        // --- login ---
        secrets.Add(("пароль регистрации/сброса", RegisterPassword));
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            login,
            password = RegisterPassword,
        });
        Assert.True(
            (int)loginResponse.StatusCode is >= 200 and < 300,
            "Шаг when не исполним: POST /api/v1/auth/login не успешен "
            + $"({(int)loginResponse.StatusCode} {loginResponse.StatusCode}) — полный auth-поток "
            + $"NFR-006(а) прерван. Тело: {await loginResponse.Content.ReadAsStringAsync()}");
        CollectCookies(loginResponse, secrets);

        // --- recovery/request (код уходит в dev-канал почты) ---
        var requestResponse = await client.PostAsJsonAsync("/api/v1/auth/recovery/request", new
        {
            email,
        });
        Assert.True(
            (int)requestResponse.StatusCode is >= 200 and < 300,
            "Шаг when не исполним: POST /api/v1/auth/recovery/request не успешен "
            + $"({(int)requestResponse.StatusCode} {requestResponse.StatusCode}) — полный auth-поток "
            + $"NFR-006(а) прерван. Тело: {await requestResponse.Content.ReadAsStringAsync()}");

        var code = CaptureRecoveryCode(factory);
        Assert.True(
            code is not null,
            "Шаг given не исполним: код восстановления не найден в записях dev-почты sink "
            + "(ожидалась запись категории с почтой). Записи sink: " + DescribeRecords(factory));
        secrets.Add(("код восстановления", code!));

        // --- recovery/confirm (код → resetToken) ---
        var confirmResponse = await client.PostAsJsonAsync("/api/v1/auth/recovery/confirm", new
        {
            email,
            code,
        });
        Assert.True(
            (int)confirmResponse.StatusCode is >= 200 and < 300,
            "Шаг when не исполним: POST /api/v1/auth/recovery/confirm не успешен "
            + $"({(int)confirmResponse.StatusCode} {confirmResponse.StatusCode}) — полный auth-поток "
            + $"NFR-006(а) прерван. Тело: {await confirmResponse.Content.ReadAsStringAsync()}");
        var confirmJson = await confirmResponse.Content.ReadAsStringAsync();
        var resetToken = ExtractStringProperty(confirmJson, "resetToken");
        Assert.True(
            resetToken is not null,
            $"Шаг when не исполним: ответ recovery/confirm без поля resetToken: {confirmJson}");
        secrets.Add(("resetToken", resetToken!));

        // --- reset-password ---
        var resetResponse = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            resetToken,
            password = RegisterPassword,
            confirmPassword = RegisterPassword,
        });
        Assert.True(
            (int)resetResponse.StatusCode is >= 200 and < 300,
            "Шаг when не исполним: POST /api/v1/auth/reset-password не успешен "
            + $"({(int)resetResponse.StatusCode} {resetResponse.StatusCode}) — полный auth-поток "
            + $"NFR-006(а) прерван. Тело: {await resetResponse.Content.ReadAsStringAsync()}");

        // --- logout (значения cookie уходят в запросе) ---
        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        var cookieHeader = string.Join(
            "; ",
            secrets.Where(item => item.Description.StartsWith("cookie ", StringComparison.Ordinal))
                .Select(item =>
                {
                    var name = item.Description["cookie ".Length..].TrimEnd(':');
                    return $"{name}={item.Value}";
                })
                .Distinct());
        if (cookieHeader.Length > 0)
        {
            logoutRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }

        var logoutResponse = await client.SendAsync(logoutRequest);
        Assert.True(
            (int)logoutResponse.StatusCode is >= 200 and < 300,
            $"Шаг when не исполним: POST /api/v1/auth/logout не успешен "
            + $"({(int)logoutResponse.StatusCode} {logoutResponse.StatusCode}; решение: logout "
            + $"всегда 204, ADR-010) — полный auth-поток NFR-006(а) прерван. "
            + $"Тело: {await logoutResponse.Content.ReadAsStringAsync()}");

        // then: проверка всех записей sink вне категории 'EmailDev'.
        var records = factory.LogSink.Snapshot();
        Assert.True(
            records.Count > 0,
            "Предусловие then: sink пуст — auth-поток не оставил ни одной записи лога.");

        var violations = new List<string>();
        var codeRecords = 0;
        foreach (var record in records)
        {
            var serialized = SerializeRecord(record);
            var inDevEmailCategory = string.Equals(record.Category, DevEmailCategory, StringComparison.Ordinal);

            foreach (var (description, value) in secrets)
            {
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (!inDevEmailCategory && serialized.Contains(value, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"запись вне '{DevEmailCategory}' содержит секрет ({description}): {serialized}");
                }
            }

            if (serialized.Contains(code!, StringComparison.Ordinal))
            {
                codeRecords++;
                if (!serialized.Contains(DevEmailMarker, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"запись с кодом восстановления без маркера {DevEmailMarker}: {serialized}");
                }
            }
        }

        Assert.True(
            codeRecords > 0,
            $"Код восстановления не найден ни в одной записи sink (ожидалась [DEV-EMAIL]-запись в "
            + $"'{DevEmailCategory}'). Записи sink: {DescribeRecords(factory)}");
        Assert.True(
            violations.Count == 0,
            "NFR-006(а) нарушен — секреты в основных категориях лога: "
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Distinct().Take(20)));
    }

    /// <summary>Значения Set-Cookie ответа — в список секретов («cookie <имя>: значение»).</summary>
    private static void CollectCookies(HttpResponseMessage response, List<(string Description, string Value)> secrets)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
        {
            return;
        }

        foreach (var header in setCookieHeaders)
        {
            var pair = header.Split(';', 2)[0];
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = pair[..separator].Trim();
            var value = pair[(separator + 1)..].Trim();
            if (value.Length > 0)
            {
                secrets.Add(("cookie " + name + ":", value));
            }
        }
    }

    /// <summary>
    /// Код восстановления из записей dev-почты sink (категория содержит «Email» или
    /// сообщение содержит [DEV-EMAIL]); код — 6 ASCII-цифр (IF-003).
    /// </summary>
    private static string? CaptureRecoveryCode(B22WebAppFactory factory)
    {
        var candidates = factory.LogSink.Snapshot()
            .Where(record => record.Category.Contains("Email", StringComparison.OrdinalIgnoreCase)
                || (record.Message?.Contains(DevEmailMarker, StringComparison.Ordinal) ?? false))
            .SelectMany(record => Regex.Matches(
                    $"{record.Category} {record.Message} "
                    + $"{string.Join(";", record.State.Select(pair => $"{pair.Key}={pair.Value}"))}",
                    @"\b\d{6}\b")
                .Select(match => match.Value))
            .Distinct()
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>Значение строкового свойства из JSON-тела ответа (wire-контракт camelCase).</summary>
    private static string? ExtractStringProperty(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String
                    ? property.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SerializeRecord(B22LogRecord record)
    {
        var payload = new
        {
            record.Category,
            Level = record.Level.ToString(),
            record.Message,
            Template = record.MessageTemplate,
            // Значения State строкуются: среди записей бывают состояния с
            // нетривиальными типами (RuntimeType в Metadata) — System.Text.Json
            // их не сериализует, а для substring-поиска секретов достаточно
            // строкового представления значений (NFR-006).
            State = record.State.ToDictionary(pair => pair.Key, pair => pair.Value?.ToString()),
            Exception = record.Exception?.Message,
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string DescribeRecords(B22WebAppFactory factory)
    {
        var records = factory.LogSink.Snapshot();
        return records.Count == 0
            ? "<нет записей>"
            : string.Join(" | ", records.Take(20).Select(SerializeRecord));
    }
}
