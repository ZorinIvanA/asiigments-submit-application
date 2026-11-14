using System.Text;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-019 «Register: лимит срабатывает до разбора и валидации тела» (negative, FR-004, FR-006, P0).
///
/// given: с одного IP X за последний час выполнено 5 запросов POST /auth/register
///        (успешных или нет — учитываются ВСЕ попытки: TryAcquire выполняется
///        первым); baseline снимка счётчика KDF (Δkdf — приращение между снимками).
/// when:  6-й POST /api/v1/auth/register с IP X с телом '{bad json'.
/// then:  429; message 'Слишком много попыток. Повторите позже'; тело не
///        разбирается и не валидируется (нет 400 VALIDATION); Δkdf=0
///        (FR-004 AC «Register: лимит до валидации»).
/// </summary>
public sealed class Ts019_RegisterLimitBeforeBodyParseTests : IClassFixture<B09WebAppFactory>
{
    private const string TestIp = "10.0.0.19";
    private const string BrokenJsonBody = "{bad json";

    private readonly B09WebAppFactory _factory;

    public Ts019_RegisterLimitBeforeBodyParseTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SixthRegisterFromSameIp_Returns429_WithoutParsingBodyOrKdf()
    {
        // given: 5 запросов регистрации с IP X (битый JSON — невалидные попытки
        // тоже заполняют ключ 'IP'); ни одна не должна упереться в лимит.
        var client = B09HostClients.Create(_factory);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await PostFromIpAsync(client, BrokenJsonBody);
            Assert.False(
                response.StatusCode == HttpStatusCode.TooManyRequests,
                $"Предусловие: попытка {attempt + 1} из 5 не должна исчерпать лимит регистраций.");
        }

        // given: baseline счётчика KDF («счётчик обнулён» — Δ между снимками).
        var before = B09KdfSeams.KdfSnapshot(_factory);

        // when: 6-й POST /api/v1/auth/register с IP X с телом '{bad json'.
        using var sixth = await PostFromIpAsync(client, BrokenJsonBody);

        // then: 429 с дословным message (не 400 VALIDATION — тело не разбирается
        // и не валидируется).
        var body = await B09Assertions.ParseObjectAsync(
            sixth,
            HttpStatusCode.TooManyRequests,
            "6-й POST /auth/register с IP X с битым JSON");
        B09Assertions.MessageIs(body, ContractTexts.RateLimited);

        // then: Δkdf=0 — 429 не выполняет дериваций.
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
