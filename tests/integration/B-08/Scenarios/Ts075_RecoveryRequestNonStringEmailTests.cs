using LabsApp.IntegrationTests.B08.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-075 «Нестрочное значение email: всегда 200» (negative, FR-017, P1).
///
/// given: —.
/// when:  POST /auth/recovery/request с телом {email: 12345} (JSON-число).
/// then:  200 с пустым телом-объектом {}. FR-017: «ВСЕГДА отвечать 200 … в т.ч.
///        для неизвестного, невалидного по формату и нестрочного значения»;
///        нормализация ключа лимитера: «нестрока/отсутствие → ''».
/// </summary>
public sealed class Ts075_RecoveryRequestNonStringEmailTests : IClassFixture<B08WebAppFactory>
{
    private const string RequestBody = """{"email":12345}""";

    private readonly B08WebAppFactory _factory;

    public Ts075_RecoveryRequestNonStringEmailTests(B08WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RecoveryRequest_WithNonStringEmail_ReturnsAlways200EmptyObject()
    {
        // given: — (свежий хост фикстуры).

        // when: POST /auth/recovery/request с телом {email: 12345} (JSON-число).
        using var client = B08Host.CreateClient(_factory);
        using var response = await client.PostAsync(B08Host.RecoveryRequestEndpoint, RequestBody);

        // then: 200 с пустым телом-объектом {}.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        ResponseAssertions.AssertEmptyBody(body, "recovery/request (нестрочное значение email)");
    }
}
