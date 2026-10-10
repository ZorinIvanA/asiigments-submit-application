using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-073 «Лимит запросов кода 3/час по email_ci: 4-я → 429» (boundary,
/// FR-017 + FR-080, P0).
///
/// given: выполнено 3 запроса кода по email a@b.ru за последний час.
/// when:  4-й запрос с 'A@B.RU' (ci-эквивалентный адрес — тот же ключ).
/// then:  429 «Слишком много попыток. Повторите позже». FR-017 AC «Лимит
///        запросов (граница)»: при лимите 3 допущены первые 3, 4-я отклоняется.
/// </summary>
public sealed class Ts073_RecoveryRequestLimitTests : IClassFixture<B08WebAppFactory>
{
    private const string Login = "ts073-student";
    private const string Email = "a@b.ru";
    private const string FullName = "Студент СемьдесятТри";

    private readonly B08WebAppFactory _factory;

    public Ts073_RecoveryRequestLimitTests(B08WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FourthRequest_WithCiEquivalentEmail_IsRateLimited()
    {
        // given: выполнено 3 запроса кода по email a@b.ru за последний час.
        using var client = B08Host.CreateClient(_factory);
        B08Host.SeedStudent(_factory, Login, Email, FullName);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var allowed = await client.PostAsync(
                B08Host.RecoveryRequestEndpoint,
                $$"""{"email":"a@b.ru"}""");
            Assert.True(
                allowed.StatusCode == HttpStatusCode.OK,
                $"Предусловие: запрос #{attempt} должен быть допущен (200), фактически {(int)allowed.StatusCode}: {await allowed.Content.ReadAsStringAsync()}");
        }

        // when: 4-й запрос с 'A@B.RU' (ci-эквивалентный адрес — тот же ключ лимитера).
        using var fourth = await client.PostAsync(B08Host.RecoveryRequestEndpoint, """{"email":"A@B.RU"}""");

        // then: 429 «Слишком много попыток. Повторите позже».
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        var body = await fourth.Content.ReadAsStringAsync();
        ResponseAssertions.AssertMessageEquals(body, B08Host.RateLimitedMessage, "4-й recovery/request");
    }
}
