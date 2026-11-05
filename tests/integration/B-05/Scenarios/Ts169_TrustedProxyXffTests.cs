using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-169 (P0, happy_path; FR-080) «Доверенный прокси различает клиентов по
/// X-Forwarded-For».
///
/// given: ForwardedHeaders__KnownProxies=127.0.0.1 (фикстура B05ProxyWebAppFactory);
///        соединение с 127.0.0.1 (адрес соединения TestServer).
/// when:  5 регистраций с X-Forwarded-For: 203.0.113.7; затем запрос с
///        X-Forwarded-For: 198.51.100.9.
/// then:  запрос с 198.51.100.9 не блокирован лимитом 203.0.113.7 (независимые
///        счётчики по итоговому адресу). FR-080 AC «Доверенный прокси
///        различает клиентов».
/// </summary>
public sealed class Ts169_TrustedProxyXffTests(B05ProxyWebAppFactory factory)
    : IClassFixture<B05ProxyWebAppFactory>
{
    private readonly B05ProxyWebAppFactory _factory = factory;

    [Fact]
    public async Task TS169_TrustedProxyXff_ClientsHaveIndependentCounters()
    {
        // given/when: 5 регистраций через доверенный прокси с итоговым адресом 203.0.113.7.
        using var client = B05SecurityClients.CreateClient(_factory);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var request = PostRegisterWithXff(
                $"ts169-proxy-{attempt:00}", "203.0.113.7");
            using var response = await client.SendAsync(request);
            Assert.True(
                response.StatusCode == HttpStatusCode.Created,
                $"Предусловие кейса: регистрация {attempt} (итоговый адрес 203.0.113.7) должна вернуть 201 " +
                $"(лимит 5/час не исчерпан), фактически {(int)response.StatusCode}: " +
                $"{await response.Content.ReadAsStringAsync()}");
        }

        // when: запрос с X-Forwarded-For: 198.51.100.9 (новый итоговый адрес).
        using var nextRequest = PostRegisterWithXff("ts169-other-client", "198.51.100.9");
        using var next = await client.SendAsync(nextRequest);

        // then: не блокирован лимитом 203.0.113.7 (не 429) — регистрация проходит.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, next.StatusCode);
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);

        // then: независимые счётчики по итоговым адресам (FR-080 AC:
        //       «Ключи лимитера — 203.0.113.7 и 198.51.100.9»).
        var marks = B05SecurityClients.RegisterLimiterWindowMarks(_factory);
        Assert.True(
            marks.TryGetValue("203.0.113.7", out var proxyClientMarks) && proxyClientMarks == 5,
            $"По итоговому адресу 203.0.113.7 ожидалось 5 меток, фактически " +
            $"{(marks.TryGetValue("203.0.113.7", out var actualProxy) ? actualProxy : "<ключ отсутствует>")}. " +
            $"Полный словарь: [{string.Join(", ", marks.Select(pair => $"{pair.Key}={pair.Value}"))}].");
        Assert.True(
            marks.TryGetValue("198.51.100.9", out var otherClientMarks) && otherClientMarks == 1,
            $"По итоговому адресу 198.51.100.9 ожидается 1 метка, фактически " +
            $"{(marks.TryGetValue("198.51.100.9", out var actualOther) ? actualOther : "<ключ отсутствует>")}. " +
            $"Полный словарь: [{string.Join(", ", marks.Select(pair => $"{pair.Key}={pair.Value}"))}].");
    }

    /// <summary>POST /auth/register с валидным телом и заданным X-Forwarded-For.</summary>
    private static HttpRequestMessage PostRegisterWithXff(string login, string xff)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, B05SecurityClients.RegisterEndpoint)
        {
            Content = JsonContent.Create(new
            {
                fullName = "Доверенный Прокси " + login,
                login,
                email = $"{login}@example.com",
                password = B05SecurityClients.CasePassword,
                repeatPassword = B05SecurityClients.CasePassword,
            }),
        };
        request.Headers.Add("X-Forwarded-For", xff);
        return request;
    }
}
