using System.Globalization;
using System.Text;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-194 «Scope: X-Forwarded-For не влияет на ключ IP лимитера» (scope, FR-004, P1).
///
/// given: известен RemoteIpAddress тестового стенда (loopback — подменяется
///        capture-middleware фабрики фиксированным значением
///        B10AuthWebAppFactory.SubstituteRemoteIp, CR-002); запросы
///        POST /auth/register с этого IP ещё не выполнялись в окне (свежий хост
///        фикстуры — лимит регистраций пуст).
/// when:  5 запросов POST /api/v1/auth/register с пятью разными заголовками
///        X-Forwarded-For; 6-й запрос с ещё одним новым X-Forwarded-For.
/// then:  6-й — 429 RATE_LIMITED 'Слишком много попыток. Повторите позже':
///        IP = Connection.RemoteIpAddress (все шесть запросов с одного loopback,
///        различающиеся XFF не разнесли попытки по разным ключам), заголовок
///        X-Forwarded-For игнорируется (out_of_scope «Обработка X-Forwarded-For»).
/// </summary>
public sealed class Ts194_RegisterIgnoresForwardedForTests : IClassFixture<B10AuthWebAppFactory>
{
    private const string RegisterEndpoint = "/api/v1/auth/register";
    private const string ForwardedForHeader = "X-Forwarded-For";
    private const int AttemptCount = 5;

    /// <summary>Разные подменные X-Forwarded-For для всех шести запросов кейса.</summary>
    private static readonly string[] ForwardedForValues =
    {
        "203.0.113.7",
        "198.51.100.9",
        "203.0.113.77",
        "198.51.100.11",
        "203.0.113.99",
        "203.0.113.111",
    };

    private readonly B10AuthWebAppFactory _factory;

    public Ts194_RegisterIgnoresForwardedForTests(B10AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SixthRegisterFromLoopbackWithNewForwardedFor_Returns429RateLimited()
    {
        using var client = HostClients.Create(_factory);

        // given/when: 5 запросов с пятью РАЗНЫМИ X-Forwarded-For — лимит ещё не
        // исчерпан (попытки с этого IP в окне не выполнялись).
        for (var attempt = 0; attempt < AttemptCount; attempt++)
        {
            using var response = await PostRegisterAsync(client, attempt);
            Assert.True(
                response.StatusCode != HttpStatusCode.TooManyRequests,
                $"Предусловие: попытка {attempt + 1} из {AttemptCount} (X-Forwarded-For " +
                $"{ForwardedForValues[attempt]}) не должна исчерпать лимит регистраций, получен 429.");
        }

        // when: 6-й запрос с ещё одним новым X-Forwarded-For.
        using var sixth = await PostRegisterAsync(client, AttemptCount);

        // then: 6-й — 429 RATE_LIMITED с дословным сообщением.
        var body = await sixth.Content.ReadAsStringAsync();
        Assert.True(
            sixth.StatusCode == HttpStatusCode.TooManyRequests,
            $"6-й POST /auth/register с новым X-Forwarded-For должен получить 429 RATE_LIMITED " +
            $"(ключ лимитера — Connection.RemoteIpAddress, заголовок X-Forwarded-For игнорируется); " +
            $"фактически {(int)sixth.StatusCode} {sixth.StatusCode}, тело: {body}.");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.True(
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("message", out var message)
            && string.Equals(message.GetString(), ErrorTexts.RateLimited, StringComparison.Ordinal),
            $"Тело 429 должно содержать message «{ErrorTexts.RateLimited}», фактически: {body}.");

        // then: IP = Connection.RemoteIpAddress — все запросы с одного подменного
        // loopback стенда (CR-002: TestServer не заполняет RemoteIpAddress, фабрика
        // подставляет фиксированное loopback-значение), заголовки X-Forwarded-For
        // на ключ не влияли (иначе 6-й был бы допущен).
        var substituted = B10AuthWebAppFactory.SubstituteRemoteIp.ToString();
        Assert.True(
            B10AuthWebAppFactory.IsLoopback(substituted),
            $"Шаг given: RemoteIpAddress тестового стенда должен быть loopback, " +
            $"подменное значение фабрики «{substituted}».");
        var observed = _factory.ObservedRemoteIps;
        Assert.True(
            observed.Count >= AttemptCount + 1,
            $"Сервер должен увидеть не менее {AttemptCount + 1} запросов, зафиксировано {observed.Count}.");
        Assert.All(observed, remoteIp =>
            Assert.True(
                string.Equals(remoteIp, substituted, StringComparison.Ordinal),
                $"RemoteIpAddress запроса должен равняться подменному loopback стенда " +
                $"«{substituted}» (ключ лимитера — Connection.RemoteIpAddress), «{remoteIp}»."));
        Assert.True(
            observed.Distinct().Count() == 1,
            "Все запросы кейса идут с одного RemoteIpAddress, фактически: " +
            $"{string.Join(", ", observed.Distinct())}.");
    }

    /// <summary>
    /// POST /api/v1/auth/register с контрактом регистрации (fullName, login, email,
    /// password, repeatPassword — уникальные на попытку) и подменным
    /// X-Forwarded-For; RemoteIpAddress подменяется фабрикой фиксированным
    /// loopback-значением стенда (CR-002).
    /// </summary>
    private async Task<HttpResponseMessage> PostRegisterAsync(HttpClient client, int attempt)
    {
        var unique = attempt.ToString(CultureInfo.InvariantCulture);
        var json = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"fullName\":\"Тест Регистраций {unique}\"," +
            $"\"login\":\"b10ts194.user{unique}\"," +
            $"\"email\":\"b10ts194.user{unique}@example.com\"," +
            $"\"password\":\"Passw0rd!\"," +
            $"\"repeatPassword\":\"Passw0rd!\"}}");
        var request = new HttpRequestMessage(HttpMethod.Post, RegisterEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(ForwardedForHeader, ForwardedForValues[attempt]);
        return await client.SendAsync(request);
    }
}
