using System.Text;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-022 «Register: лимит срабатывает до разбора и валидации тела»
/// (negative, FR-004 + FR-006, P0).
///
/// given: с одного IP тестового хоста выполнены 5 запросов POST /auth/register
///        за час (успешных или нет — учитываются все попытки); лимит 5/3600
///        исчерпан; baseline снимка счётчика KDF.
/// when:  6-й POST /auth/register с синтаксически невалидным телом '{bad json'.
/// then:  429 RATE_LIMITED, message 'Слишком много попыток. Повторите позже';
///        НЕ 400 — тело не разбирается и не валидируется (TryAcquire первым);
///        Δkdf=0 (FR-004 AC «Register: лимит до валидации»).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файл прежней волны
/// зоны с совпадающим поведением (Ts019_RegisterLimitBeforeBodyParse) не
/// изменялся.
/// </summary>
public sealed class Ts022_RegisterLimitPrecedesBodyParsingTests : IClassFixture<B09WebAppFactory>
{
    private const string TestIp = "10.0.0.72";
    private const string BrokenJsonBody = "{bad json";

    private readonly B09WebAppFactory _factory;

    public Ts022_RegisterLimitPrecedesBodyParsingTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SixthRegisterWithBrokenJson_Returns429_WithoutParsingBodyOrKdf()
    {
        // given: с одного IP выполнены 5 запросов POST /auth/register за час
        // (невалидные попытки тоже заполняют ключ 'IP': TryAcquire первым).
        var client = B09HostClients.Create(_factory);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await PostFromIpAsync(client, BrokenJsonBody);
            Assert.True(
                response.StatusCode != HttpStatusCode.TooManyRequests,
                $"Предусловие: попытка {attempt + 1} из 5 не должна исчерпать лимит регистраций.");
        }

        // given: baseline счётчика KDF («счётчик обнулён» — Δ между снимками).
        var before = B09KdfSeams.KdfSnapshot(_factory);

        // when: 6-й POST /auth/register с синтаксически невалидным телом '{bad json'.
        using var sixth = await PostFromIpAsync(client, BrokenJsonBody);

        // then: 429 RATE_LIMITED с дословным message (НЕ 400 — тело не
        // разбирается и не валидируется).
        var body = await B09Assertions.ParseObjectAsync(
            sixth,
            HttpStatusCode.TooManyRequests,
            "6-й POST /auth/register с битым JSON при исчерпанном лимите 5/3600");
        B09Assertions.MessageIs(body, B09AuthSupport.RateLimitedMessage);

        // then: Δkdf=0 — отклонённый запрос дериваций не выполняет.
        var after = B09KdfSeams.KdfSnapshot(_factory);
        Assert.True(
            B09KdfSeams.DeltaTotal(before, after) == 0,
            $"Δkdf на 429 должен быть 0, фактически: {B09KdfSeams.DeltaBreakdown(before, after)}.");
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
