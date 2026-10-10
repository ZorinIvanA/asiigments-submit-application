using System.Globalization;
using System.Text;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-198 «X-Forwarded-For не влияет на ключ лимитера» (scope, FR-004, P2;
/// ASM-013, out_of_scope «Обработка X-Forwarded-For»).
///
/// given: с одного реального IP выполнены 5 регистраций (лимит register 5/3600
///        исчерпан; RemoteIpAddress тестового стенда — loopback, подменяемый
///        capture-middleware фабрики фиксированным значением, CR-002: TestServer
///        оставляет RemoteIpAddress равным null).
/// when:  6-й POST /auth/register со spoofed-заголовком X-Forwarded-For: 1.2.3.4.
/// then:  429 RATE_LIMITED (ключ строится по Connection.RemoteIpAddress,
///        заголовки прокси игнорируются).
/// </summary>
public sealed class B10Ts198_RegisterKeyIgnoresForwardedForTests : IClassFixture<B10AuthWebAppFactory>
{
    private const string RegisterEndpoint = "/api/v1/auth/register";
    private const string ForwardedForHeader = "X-Forwarded-For";
    private const int AttemptCount = 5;

    /// <summary>Spoofed-заголовок 6-го запроса (дословно из кейса TS-198).</summary>
    private const string SpoofedForwardedFor = "1.2.3.4";

    /// <summary>Разные подменные X-Forwarded-For пяти регистраций шага given.</summary>
    private static readonly string[] ForwardedForValues =
    {
        "203.0.113.7",
        "198.51.100.9",
        "203.0.113.77",
        "198.51.100.11",
        "203.0.113.99",
    };

    private readonly B10AuthWebAppFactory _factory;

    public B10Ts198_RegisterKeyIgnoresForwardedForTests(B10AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SixthRegisterWithSpoofedForwardedFor_Returns429RateLimited()
    {
        using var client = HostClients.Create(_factory);

        // given: 5 регистраций с одного реального IP (подменный loopback стенда) —
        // лимит register 5/3600 исчерпан; различающиеся X-Forwarded-For ключ не
        // размели (иначе каждая регистрация получила бы собственный лимит).
        for (var attempt = 0; attempt < AttemptCount; attempt++)
        {
            using var response = await PostRegisterAsync(client, attempt, ForwardedForValues[attempt]);
            Assert.True(
                response.StatusCode == HttpStatusCode.Created,
                $"Шаг given: регистрация {attempt + 1} из {AttemptCount} (X-Forwarded-For " +
                $"{ForwardedForValues[attempt]}) должна пройти (201), фактически " +
                $"{(int)response.StatusCode} {response.StatusCode}.");
        }

        // when: 6-й POST /auth/register со spoofed-заголовком X-Forwarded-For: 1.2.3.4.
        using var sixth = await PostRegisterAsync(client, AttemptCount, SpoofedForwardedFor);

        // then: 429 RATE_LIMITED с дословным сообщением — заголовки прокси
        // игнорируются, ключ строится по Connection.RemoteIpAddress.
        var body = await sixth.Content.ReadAsStringAsync();
        Assert.True(
            sixth.StatusCode == HttpStatusCode.TooManyRequests,
            $"6-й POST /auth/register с X-Forwarded-For: {SpoofedForwardedFor} должен получить " +
            $"429 RATE_LIMITED (ключ лимитера — Connection.RemoteIpAddress, заголовки прокси " +
            $"игнорируются), фактически {(int)sixth.StatusCode} {sixth.StatusCode}, тело: {body}.");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.True(
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("message", out var message)
            && string.Equals(message.GetString(), ErrorTexts.RateLimited, StringComparison.Ordinal),
            $"Тело 429 должно содержать message «{ErrorTexts.RateLimited}», фактически: {body}.");

        // then: ключ построен по Connection.RemoteIpAddress — все шесть запросов
        // обслужены с одного и того же подменного loopback-значения стенда
        // (CR-002), различающиеся X-Forwarded-For на ключ не влияли.
        var substituted = B10AuthWebAppFactory.SubstituteRemoteIp.ToString();
        Assert.True(
            B10AuthWebAppFactory.IsLoopback(substituted),
            $"Шаг given: реальный IP тестового стенда должен быть loopback, подменное " +
            $"значение фабрики «{substituted}».");
        var observed = _factory.ObservedRemoteIps;
        Assert.True(
            observed.Count >= AttemptCount + 1,
            $"Сервер должен увидеть не менее {AttemptCount + 1} запросов, зафиксировано {observed.Count}.");
        Assert.All(observed, remoteIp =>
            Assert.True(
                string.Equals(remoteIp, substituted, StringComparison.Ordinal),
                $"RemoteIpAddress запроса должен равняться реальному IP стенда «{substituted}» " +
                $"(ключ лимитера — Connection.RemoteIpAddress), «{remoteIp}»."));
        Assert.True(
            observed.Distinct().Count() == 1,
            "Все запросы кейса идут с одного реального IP, фактически: " +
            $"{string.Join(", ", observed.Distinct())}.");
    }

    /// <summary>
    /// POST /api/v1/auth/register с контрактом регистрации (fullName, login,
    /// email, password, repeatPassword — уникальные на попытку, чтобы все пять
    /// регистраций шага given прошли) и подменным X-Forwarded-For.
    /// </summary>
    private async Task<HttpResponseMessage> PostRegisterAsync(
        HttpClient client, int attempt, string forwardedFor)
    {
        var unique = attempt.ToString(CultureInfo.InvariantCulture);
        var json = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"fullName\":\"Тест Лимитера {unique}\"," +
            $"\"login\":\"b10ts198.user{unique}\"," +
            $"\"email\":\"b10ts198.user{unique}@example.com\"," +
            $"\"password\":\"Passw0rd!\"," +
            $"\"repeatPassword\":\"Passw0rd!\"}}");
        var request = new HttpRequestMessage(HttpMethod.Post, RegisterEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(ForwardedForHeader, forwardedFor);
        return await client.SendAsync(request);
    }
}
