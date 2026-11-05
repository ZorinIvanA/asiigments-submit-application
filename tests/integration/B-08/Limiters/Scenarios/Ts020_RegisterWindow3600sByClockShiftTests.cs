using LabsApp.IntegrationTests.B08.Limiters.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-020 «Register: окно 3600 с доказано переводом часов» (boundary,
/// FR-004, P0). Бизнес-время — FakeTimeProvider фикстуры
/// B08LimitersRateLimitWebAppFactory (ADR-002: единственный источник
/// бизнес-времени инжектируется в движок лимитера); часы стартуют в момент
/// создания фикстуры и сдвигаются только вперёд (Advance), поэтому T0 —
/// момент пяти меток (старт часов теста).
///
/// given: 5 меток регистрационного лимита ключа 'IP' поставлены в момент T0
///        (5 успешных POST /auth/register с одного IP); инжектируемые часы
///        переведены на T0+3600001 мс; подготовлены валидные и свободные
///        логин/email для 6-й регистрации.
/// when:  6-й POST /auth/register с валидным телом.
/// then:  не 429 — 201: метки T0 вычищены (строго старше границы окна);
///        окно ровно 3600 с (AC FR-004 матрица: register 5/3600 с по IP).
/// </summary>
public sealed class Ts020_RegisterWindow3600sByClockShiftTests : IClassFixture<B08LimitersRateLimitWebAppFactory>
{
    private const string ClientIp = "10.19.8.31";

    private readonly B08LimitersRateLimitWebAppFactory _factory;

    public Ts020_RegisterWindow3600sByClockShiftTests(B08LimitersRateLimitWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_SixthAttemptAfter3600001ms_CreatedNot429()
    {
        // given: 5 успешных регистраций с одного IP — 5 меток регистрационного
        // лимита ключа 'IP' в момент T0 (T0 = старт инжектируемых часов).
        using var client = B08LimitersClients.CreateClientWithIp(_factory, ClientIp);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var created = await client.PostAsJsonAsync(B08LimitersClients.RegisterEndpoint, new
            {
                fullName = $"Register Window {attempt:D2}",
                login = $"b08ts020user{attempt:D2}",
                email = $"b08ts020user{attempt:D2}@example.com",
                password = B08LimitersClients.TestUserPassword,
                repeatPassword = B08LimitersClients.TestUserPassword,
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        // given: инжектируемые часы переведены на T0+3600001 мс (вперёд —
        // механика FakeTimeProvider фикстуры).
        _factory.Time.Advance(TimeSpan.FromMilliseconds(3_600_001));

        // when: 6-й POST /auth/register с валидным телом (свободные логин/email).
        using var sixth = await client.PostAsJsonAsync(B08LimitersClients.RegisterEndpoint, new
        {
            fullName = "Register Window Sixth",
            login = "b08ts020user06",
            email = "b08ts020user06@example.com",
            password = B08LimitersClients.TestUserPassword,
            repeatPassword = B08LimitersClients.TestUserPassword,
        });

        // then: не 429 — 201: метки T0 вычищены (строго старше границы
        // now−windowMs), окно ровно 3600 с по IP.
        Assert.Equal(HttpStatusCode.Created, sixth.StatusCode);
    }
}
