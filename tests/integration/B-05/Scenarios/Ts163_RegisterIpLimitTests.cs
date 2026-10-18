using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-163 (P0, boundary; FR-080, FR-012) «Лимит регистраций 5/час на IP:
/// 6-я → 429, метка отказа не добавляется».
///
/// given: RemoteIpAddress=10.0.0.1 (заголовок харнеса — контракт IF-006);
///        выполнено 5 любых попыток регистрации (2 успешные + 3 неуспешные) за час.
/// when:  6-я попытка с того же IP с мусорным телом; отдельно 6-я с
///        RemoteIpAddress=10.0.0.2 (валидное тело); инспекция словаря лимитера
///        через DI.
/// then:  6-я с 10.0.0.1 → 429 «Слишком много попыток. Повторите позже» (любое
///        тело, включая невалидное); с 10.0.0.2 → не 429; в словаре по ключу
///        10.0.0.1 ровно 5 меток — отклонённая попытка метку НЕ добавила.
///        FR-080 AC «Регистрация на IP»; глоссарий «Скользящее окно».
/// </summary>
public sealed class Ts163_RegisterIpLimitTests(B05SecurityWebAppFactory factory)
    : IClassFixture<B05SecurityWebAppFactory>
{
    private const string LimitedIp = "10.0.0.1";
    private const string FreshIp = "10.0.0.2";

    private readonly B05SecurityWebAppFactory _factory = factory;

    [Fact]
    public async Task TS163_SixthRegisterFromSameIp_Is429AndRejectedAttemptAddsNoMark()
    {
        // given: 5 любых попыток регистрации с IP 10.0.0.1 (2 успешные + 3 неуспешные).
        using var limitedClient = B05SecurityClients.CreateClientWithIp(_factory, LimitedIp);

        using (var ok1 = await B05SecurityClients.PostRegisterAsync(
                   limitedClient, "Лимит Первый", "ts163-user-1", "ts163-user-1@example.com"))
        {
            Assert.Equal(HttpStatusCode.Created, ok1.StatusCode);
        }

        using (var ok2 = await B05SecurityClients.PostRegisterAsync(
                   limitedClient, "Лимит Второй", "ts163-user-2", "ts163-user-2@example.com"))
        {
            Assert.Equal(HttpStatusCode.Created, ok2.StatusCode);
        }

        for (var attempt = 3; attempt <= 5; attempt++)
        {
            // Невалидное тело: пустое fullName → 400 (попытка учитывается лимитером).
            using var invalid = await limitedClient.PostAsJsonAsync(
                B05SecurityClients.RegisterEndpoint, new { fullName = "" });
            Assert.True(
                invalid.StatusCode == HttpStatusCode.BadRequest,
                $"Предусловие кейса: неуспешная попытка {attempt} должна дать 400, " +
                $"фактически {(int)invalid.StatusCode}: {await invalid.Content.ReadAsStringAsync()}");
        }

        // when: 6-я попытка с того же IP с мусорным телом (429 раньше валидации).
        using var sixth = await limitedClient.PostAsJsonAsync(
            B05SecurityClients.RegisterEndpoint, new { fullName = "" });

        // then: 429 «Слишком много попыток. Повторите позже» (не 400).
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(sixth);
        BodyAssertions.MessageIs(root, B05SecurityClients.RateLimitedMessage);

        // when/then: 6-я попытка с IP 10.0.0.2 (валидное тело) — не 429.
        using var freshClient = B05SecurityClients.CreateClientWithIp(_factory, FreshIp);
        using var fresh = await B05SecurityClients.PostRegisterAsync(
            freshClient, "Лимит Другой Айпи", "ts163-user-fresh", "ts163-user-fresh@example.com");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, fresh.StatusCode);
        Assert.Equal(HttpStatusCode.Created, fresh.StatusCode);

        // then: в словаре лимитера по ключу 10.0.0.1 ровно 5 меток —
        //       отклонённая (429) попытка метку НЕ добавила.
        var marks = B05SecurityClients.RegisterLimiterWindowMarks(_factory);
        Assert.True(
            marks.TryGetValue(LimitedIp, out var limitedMarks) && limitedMarks == 5,
            $"По ключу {LimitedIp} ожидалось ровно 5 меток (отказ не добавляет метку), " +
            $"фактически {(marks.TryGetValue(LimitedIp, out var actual) ? actual : "<ключ отсутствует>")}. " +
            $"Полный словарь: [{string.Join(", ", marks.Select(pair => $"{pair.Key}={pair.Value}"))}].");
        Assert.True(
            marks.TryGetValue(FreshIp, out var freshMarks) && freshMarks == 1,
            $"По ключу {FreshIp} ожидается 1 метка успешной регистрации, фактически " +
            $"{(marks.TryGetValue(FreshIp, out var actualFresh) ? actualFresh : "<ключ отсутствует>")}. " +
            $"Полный словарь: [{string.Join(", ", marks.Select(pair => $"{pair.Key}={pair.Value}"))}].");
    }
}
