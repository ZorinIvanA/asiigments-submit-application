using System.Net.Http;
using System.Text;
using System.Text.Json;
using LabsApp.IntegrationTests.B20.Infrastructure;

namespace LabsApp.IntegrationTests.B20.Scenarios;

/// <summary>
/// Кейс батча B-20: TS-144 «Клиент: dev-прокси ведёт /api и /health на
/// Kestrel» — реализован этим классом (канонический файл клиентской зоны B-19
/// Ts180_ProxyDevServerTests.cs переименован на префикс Ts144 при переиздании
/// реестра).
/// TS-144 (happy_path, P0, FR-026 AC «Прокси»).
/// given: запущены Kestrel (http://localhost:5080, Development с сидом) и
///        ng serve с src/client/proxy.conf.json;
/// when:  запрос из dev-сервера на /api/v1/auth/login (валидные креды
///        teacher/teacher123!) и на /health;
/// then:  оба достигают Kestrel: вход — 200 MeDto; /health — 200
///        {'status':'ok'}; proxy.conf.json маршрутизирует /api и /health на
///        http://localhost:5080 (FR-026 AC «Прокси»).
/// </summary>
[Collection(SerialNgProcessCollection.Name)]
public sealed class Ts144_ProxyDevServerTests : IClassFixture<B20DevStackFixture>
{
    private readonly B20DevStackFixture _stack;

    public Ts144_ProxyDevServerTests(B20DevStackFixture stack)
    {
        _stack = stack;
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

        // then: 200 MeDto (login/role из домена User).
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
                + "(ожидается объект с login/role — IF-018/домен User). "
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
