using LabsApp.IntegrationTests.B08.Limiters.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-022 «Recovery/request: окно 3600 с доказано переводом часов»
/// (boundary, FR-004 + FR-012, P0). Бизнес-время — FakeTimeProvider фикстуры
/// B08LimitersRateLimitWebAppFactory (ADR-002); часы стартуют в момент
/// создания фикстуры и сдвигаются только вперёд (Advance), поэтому T0 —
/// момент трёх меток (старт часов теста).
///
/// given: 3 метки лимита recovery_request ключа 'lower(email)' поставлены в
///        T0 (3 запроса на один email); инжектируемые часы переведены на
///        T0+3600001 мс.
/// when:  4-й POST /auth/recovery/request на тот же email.
/// then:  200 с пустым телом — не 429: метки T0 вычищены, окно ровно 3600 с
///        (AC FR-004 матрица: recovery_request 3/3600 с).
/// </summary>
public sealed class Ts022_RecoveryWindow3600sByClockShiftTests : IClassFixture<B08LimitersRateLimitWebAppFactory>
{
    private const string Email = "b08ts022windowed@example.com";

    private readonly B08LimitersRateLimitWebAppFactory _factory;

    public Ts022_RecoveryWindow3600sByClockShiftTests(B08LimitersRateLimitWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RecoveryRequest_FourthRequestAfter3600001ms_200WithEmptyBody()
    {
        // given: 3 запроса на один email — 3 метки ключа 'lower(email)' в
        // момент T0 (T0 = старт инжектируемых часов).
        using var client = B08LimitersClients.CreateClientWithoutCookies(_factory);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = await client.PostAsJsonAsync(
                B08LimitersClients.RecoveryRequestEndpoint, new { email = Email });
            Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        }

        // given: инжектируемые часы переведены на T0+3600001 мс (вперёд —
        // механика FakeTimeProvider фикстуры).
        _factory.Time.Advance(TimeSpan.FromMilliseconds(3_600_001));

        // when: 4-й POST /auth/recovery/request на тот же email.
        using var fourth = await client.PostAsJsonAsync(
            B08LimitersClients.RecoveryRequestEndpoint, new { email = Email });

        // then: 200 с пустым телом — не 429: метки T0 вычищены (строго старше
        // границы now−windowMs), окно ровно 3600 с.
        Assert.Equal(HttpStatusCode.OK, fourth.StatusCode);
        Assert.Equal(string.Empty, await fourth.Content.ReadAsStringAsync());
    }
}
