using System.Text;
using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-022 «Register: лимит срабатывает до разбора и валидации тела»
/// (negative, FR-004 + FR-006, P0).
///
/// given: с одного IP тестового хоста выполнены 5 запросов POST /auth/register
///        за час (успешных или нет — TryAcquire первым); лимит 5/3600 исчерпан.
/// when:  6-й POST /auth/register с синтаксически невалидным телом '{bad json'.
/// then:  429 RATE_LIMITED, message 'Слишком много попыток. Повторите позже';
///        НЕ 400 — тело не разбирается и не валидируется (TryAcquire первым;
///        FR-004 AC «Register: лимит до валидации»).
///
/// Файл волны батча B-09 (кейс — закон; файлы прежних волн зоны с совпадающим
/// поведением не изменялись).
/// </summary>
public sealed class Ts022_RegisterLimiterBeforeBodyParse429Tests : IClassFixture<B09WebAppFactory>
{
    private const string TestIp = "10.90.0.22";
    private const string BrokenJsonBody = "{bad json";

    private readonly B09WebAppFactory _factory;

    public Ts022_RegisterLimiterBeforeBodyParse429Tests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SixthRegisterWithBrokenJson_Is429RateLimited_Not400Validation()
    {
        // given: с одного IP выполнены 5 запросов POST /auth/register за час
        // (каждая попытка учитывается лимитером — TryAcquire до разбора тела;
        // ни одна из пяти не должна исчерпать лимит).
        using var client = B09HostClients.Create(_factory);
        for (var attempt = 0; attempt < RegisterLimiter.RegisterLimit; attempt++)
        {
            using var response = await PostFromIpAsync(client, BrokenJsonBody);
            Assert.True(
                response.StatusCode != HttpStatusCode.TooManyRequests,
                $"Предусловие: попытка {attempt + 1} из 5 не должна упираться в лимит регистраций.");
        }

        // when: 6-й POST /auth/register с синтаксически невалидным телом.
        using var sixth = await PostFromIpAsync(client, BrokenJsonBody);

        // then: НЕ 400 — тело не разбирается и не валидируется (TryAcquire
        // выполняется первым, лимит исчерпан).
        Assert.NotEqual(HttpStatusCode.BadRequest, sixth.StatusCode);

        // then: 429 RATE_LIMITED с дословным message.
        var body = await B09Assertions.ParseObjectAsync(
            sixth,
            HttpStatusCode.TooManyRequests,
            "6-й POST /auth/register с битым JSON при исчерпанном лимите 5/3600");
        B09Assertions.MessageIs(body, B09AuthSupport.RateLimitedMessage);
    }

    /// <summary>POST c сырым телом и тестовой подменой RemoteIpAddress (ключ лимитера — IP).</summary>
    private Task<HttpResponseMessage> PostFromIpAsync(HttpClient client, string rawBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, B09HostClients.RegisterEndpoint)
        {
            Content = new StringContent(rawBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(B09WebAppFactory.RemoteIpHeader, TestIp);
        return client.SendAsync(request);
    }
}
