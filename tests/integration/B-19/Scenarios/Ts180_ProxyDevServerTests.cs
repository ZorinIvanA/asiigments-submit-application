using System.Net.Http;
using System.Text;
using System.Text.Json;
using LabsApp.IntegrationTests.B19.Infrastructure;

namespace LabsApp.IntegrationTests.B19.Scenarios;

/// <summary>
/// Кейс батча B-19: TS-174 «Клиент: прокси ведёт /api и /health на Kestrel» —
/// реализован этим классом (исторические ID: кейс раунда c-882 TS-144;
/// сценарий контура TS-180).
/// TS-180 «Клиент: прокси ведёт /api и /health на Kestrel» (happy_path, P2,
/// FR-026 AC «Прокси»).
/// given: запущены Kestrel (http://localhost:5080, Development-сид) и ng serve
///        с src/client/proxy.conf.json (правила /api и /health →
///        http://localhost:5080 подтверждены файлом);
/// when:  через адрес dev-сервера: POST /api/v1/auth/login
///        {login:'teacher', password:'teacher123!'} и GET /health;
/// then:  login — 200 (MeDto учителя); /health — 200 {'status':'ok'} —
///        запросы достигают Kestrel (FR-026 AC «Прокси»).
/// </summary>
[Collection(SerialNgProcessCollection.Name)]
public sealed class Ts180_ProxyDevServerTests : IClassFixture<DevStackFixture>
{
    private readonly DevStackFixture _stack;

    public Ts180_ProxyDevServerTests(DevStackFixture stack)
    {
        _stack = stack;
    }

    /// <summary>
    /// given: правила /api и /health → http://localhost:5080 подтверждены
    /// файлом src/client/proxy.conf.json (FR-026 п.5) — предусловие живого
    /// стека, без которого then-проверки ниже не имели бы силы.
    /// </summary>
    [Fact]
    public void ProxyConfigFile_RoutesApiAndHealthToKestrel()
    {
        Assert.True(
            File.Exists(RepoPaths.ProxyConfigFile),
            "given не выполнен: не найден src/client/proxy.conf.json "
            + $"({RepoPaths.ProxyConfigFile}).");

        using var json = JsonDocument.Parse(File.ReadAllText(RepoPaths.ProxyConfigFile));
        var violations = new List<string>();

        foreach (var route in new[] { "/api", "/health" })
        {
            if (!json.RootElement.TryGetProperty(route, out var entry))
            {
                violations.Add($"нет правила для '{route}'.");
                continue;
            }

            var target = entry.TryGetProperty("target", out var targetElement)
                ? targetElement.GetString()
                : null;
            if (target != "http://localhost:5080")
            {
                violations.Add(
                    $"правило '{route}' ведёт на '{target}', ожидался http://localhost:5080.");
            }
        }

        Assert.True(
            violations.Count == 0,
            "given не выполнен: proxy.conf.json не подтверждает правила "
            + "/api и /health → http://localhost:5080:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(item => $"  - {item}")));
    }

    /// <summary>then: /health через dev-сервер — 200 {'status':'ok'}.</summary>
    [Fact]
    public async Task HealthRequest_IsProxiedToKestrel()
    {
        // when: GET /health через адрес dev-сервера ng.
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await client.GetAsync($"{_stack.DevServerUrl}/health");
        var body = await response.Content.ReadAsStringAsync();

        // then: 200 {'status':'ok'}.
        Assert.True(
            (int)response.StatusCode == 200,
            $"then не выполнен: GET {nameof(_stack.DevServerUrl)}/health через прокси вернул "
            + $"HTTP {(int)response.StatusCode}, ожидался 200 (FR-026 AC «Прокси»).{Environment.NewLine}"
            + $"Тело ответа: {body}{Environment.NewLine}{_stack.DevServerOutputTail()}");

        using var json = JsonDocument.Parse(body);
        var status = json.RootElement.GetProperty("status").GetString();
        Assert.True(
            status == "ok",
            $"then не выполнен: /health вернул status='{status}', ожидался 'ok' "
            + $"(тело: {body}).");
    }

    /// <summary>then: login через dev-сервер — 200 MeDto (прокси обслуживает /api).</summary>
    [Fact]
    public async Task LoginRequest_IsProxiedToKestrel_AndReturnsMeDto()
    {
        // when: POST /api/v1/auth/login (teacher/teacher123! — сид Development)
        // через адрес dev-сервера ng.
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_stack.DevServerUrl}/api/v1/auth/login")
        {
            Content = new StringContent(
                """{"login":"teacher","password":"teacher123!"}""",
                Encoding.UTF8,
                "application/json"),
        };
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // then: 200 MeDto (login/fullName/role из домена User).
        Assert.True(
            (int)response.StatusCode == 200,
            $"then не выполнен: POST /api/v1/auth/login через прокси вернул HTTP "
            + $"{(int)response.StatusCode}, ожидался 200 (FR-026 AC «Прокси»: login через UI даёт 200)."
            + $"{Environment.NewLine}Тело ответа: {body}{Environment.NewLine}"
            + $"Вывод Kestrel:{Environment.NewLine}{_stack.BackendOutputTail()}");

        MeDto me;
        try
        {
            using var json = JsonDocument.Parse(body);
            me = new MeDto(
                json.RootElement.GetProperty("login").GetString(),
                json.RootElement.GetProperty("role").GetString());
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException)
        {
            Assert.Fail(
                "then не выполнен: тело ответа login не является MeDto "
                + "(ожидается объект с login/fullName/role — IF-014/домен User). "
                + $"Тело: {body}. Ошибка разбора: {error.Message}");
            return;
        }

        Assert.True(
            me.Login == "teacher" && me.Role == "teacher",
            $"then не выполнен: login вернул MeDto с login='{me.Login}', role='{me.Role}', "
            + $"ожидались teacher/teacher (сид Development). Тело: {body}");
    }

    private sealed record MeDto(string? Login, string? Role);
}
