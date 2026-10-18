using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-168 (P0, negative; FR-080) «X-Forwarded-For не доверяется по умолчанию».
///
/// given: ForwardedHeaders__KnownProxies не задан (пуст — middleware не
///        подключается вовсе, доверия никому); транспортный IP соединения
///        127.0.0.1 (адрес соединения TestServer, запросы БЕЗ заголовка
///        подмены IP харнеса).
/// when:  5 регистраций с разными подделанными заголовками X-Forwarded-For;
///        затем 6-я с того же соединения.
/// then:  6-я → 429: ключ лимитера — 127.0.0.1, заголовок проигнорирован,
///        подмена XFF лимит не расширяет. FR-080 AC «X-Forwarded-For не
///        доверяется по умолчанию».
/// </summary>
public sealed class Ts168_XffNotTrustedTests(B05SecurityWebAppFactory factory)
    : IClassFixture<B05SecurityWebAppFactory>
{
    /// <summary>Единственный допустимый ключ лимитера (транспортный адрес TestServer).</summary>
    private const string TransportIp = "127.0.0.1";

    private readonly B05SecurityWebAppFactory _factory = factory;

    [Fact]
    public async Task TS168_SpoofedXffIsIgnored_SixthRegisterFromSameConnection429()
    {
        // given/when: 5 регистраций с транспортного IP 127.0.0.1, каждая со своим
        // подделанным X-Forwarded-For (если бы заголовок доверялся — ключи были бы разными).
        using var client = B05SecurityClients.CreateClient(_factory);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, B05SecurityClients.RegisterEndpoint)
            {
                Content = JsonContent.Create(new
                {
                    fullName = $"ИксФэф Попытка {attempt:00}",
                    login = $"ts168-user-{attempt:00}",
                    email = $"ts168-user-{attempt:00}@example.com",
                    password = B05SecurityClients.CasePassword,
                    repeatPassword = B05SecurityClients.CasePassword,
                }),
            };
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{attempt}");
            using var response = await client.SendAsync(request);
            Assert.True(
                response.StatusCode == HttpStatusCode.Created,
                $"Предусловие кейса: регистрация {attempt} с подменённым XFF должна вернуть 201 " +
                $"(лимит 5/час не исчерпан), фактически {(int)response.StatusCode}: " +
                $"{await response.Content.ReadAsStringAsync()}");
        }

        // when: 6-я попытка с того же соединения (валидное тело — иначе 429
        //       можно было бы объяснить валидацией тела).
        using var sixthRequest = new HttpRequestMessage(HttpMethod.Post, B05SecurityClients.RegisterEndpoint)
        {
            Content = JsonContent.Create(new
            {
                fullName = "ИксФэф Шестая",
                login = "ts168-user-06",
                email = "ts168-user-06@example.com",
                password = B05SecurityClients.CasePassword,
                repeatPassword = B05SecurityClients.CasePassword,
            }),
        };
        sixthRequest.Headers.Add("X-Forwarded-For", "203.0.113.6");
        using var sixth = await client.SendAsync(sixthRequest);

        // then: 6-я → 429 (не 201): ключ лимитера — 127.0.0.1, подмена XFF лимит не расширяет.
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(sixth);
        BodyAssertions.MessageIs(root, B05SecurityClients.RateLimitedMessage);

        // then: заголовок проигнорирован — в словаре лимитера единственный ключ
        //       127.0.0.1 с 5 метками; ключей 203.0.113.* не появилось.
        var marks = B05SecurityClients.RegisterLimiterWindowMarks(_factory);
        Assert.True(
            marks.TryGetValue(TransportIp, out var transportMarks) && transportMarks == 5,
            $"По транспортному ключу {TransportIp} ожидалось 5 меток, фактически " +
            $"{(marks.TryGetValue(TransportIp, out var actual) ? actual : "<ключ отсутствует>")}. " +
            $"Полный словарь: [{string.Join(", ", marks.Select(pair => $"{pair.Key}={pair.Value}"))}].");
        Assert.True(
            marks.Count == 1,
            "X-Forwarded-For обязан игнорироваться: ключей по подменённым адресам быть не должно, " +
            $"фактический словарь: [{string.Join(", ", marks.Select(pair => $"{pair.Key}={pair.Value}"))}].");
    }
}
