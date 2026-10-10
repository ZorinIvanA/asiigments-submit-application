using LabsApp.IntegrationTests.B17.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-072 «recovery/request: лимит 3/час на email» — вариант НЕСУЩЕСТВУЮЩЕГО
/// email (negative, FR-012 + FR-004, P0).
///
/// given: 3 запроса recovery/request на email X за последний час; X НЕ существует
///        (никто не регистрировался — оракула существования нет); квота —
///        собственная фикстура этого класса, отдельная от варианта Ts072a.
/// when:  4-й request на тот же email X.
/// then:  429; message «Слишком много попыток. Повторите позже» — тот же статус
///        и тот же текст, что и для существующего X (FR-004: «429 одинаково для
///        существующего и несуществующего email»).
/// </summary>
public sealed class Ts072b_RecoveryRequestLimitUnknownEmailTests : IClassFixture<B17WebAppFactory>
{
    private const string Email = "nobody.ts072@example.com";
    private const string RequestBody = """{"email":"nobody.ts072@example.com"}""";

    private readonly B17WebAppFactory _factory;

    public Ts072b_RecoveryRequestLimitUnknownEmailTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FourthRequestWithinHour_Is429WithSameMessageAsExistingEmail()
    {
        // given: email X не существует (given фиксируется проверкой хранилища
        //        пользователей); 3 запроса recovery/request за последний час.
        using var client = B17Host.CreateClient(_factory);
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        Assert.Null(users.GetByEmail(Email));

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var response = await B17Host.PostJsonAsync(
                client, B17Host.RecoveryRequestEndpoint, RequestBody);
            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                $"Запрос {attempt}/3 (несуществующий email) ожидался 200 (лимит 3/час не исчерпан), "
                + $"фактически {(int)response.StatusCode}.");
        }

        // when: 4-й request на тот же email X.
        using var fourth = await B17Host.PostJsonAsync(
            client, B17Host.RecoveryRequestEndpoint, RequestBody);

        // then: 429; тот же message, что и для существующего email (одни константы B17Host).
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        var content = await fourth.Content.ReadAsStringAsync();
        var message = B17Host.ExtractEnvelopeMessage(content, "recovery/request 4-й (несуществующий email)");
        Assert.True(
            message.Equals(B17Host.RateLimitedMessage, StringComparison.Ordinal),
            $"Ожидался message «{B17Host.RateLimitedMessage}» дословно (неотличимо от существующего "
            + $"email — оракула существования нет), фактически «{message}».");
    }
}
