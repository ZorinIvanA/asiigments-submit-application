using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-072 «recovery/request: лимит 3/час на email» — вариант СУЩЕСТВУЮЩЕГО email
/// (negative, FR-012 + FR-004, P0).
///
/// given: 3 запроса recovery/request на email X за последний час; X существует
///        (DI-сид); квота — собственная фикстура этого класса (свежий хост —
///        пустой словарь лимитера), несуществующий вариант — отдельный класс
///        Ts072b со своей фикстурой.
/// when:  4-й request на тот же email X.
/// then:  429; message «Слишком много попыток. Повторите позже» (FR-012 AC
///        «Лимит 3/час»; одинаковый статус и текст с вариантом Ts072b — оба
///        класса сверяются с одними константами B17Host).
/// </summary>
public sealed class Ts072a_RecoveryRequestLimitExistingEmailTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts072a-student";
    private const string Email = "student05@example.com";
    private const string FullName = "Студент СемьдесятДва";
    private const string RequestBody = """{"email":"student05@example.com"}""";

    private readonly B17WebAppFactory _factory;

    public Ts072a_RecoveryRequestLimitExistingEmailTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FourthRequestWithinHour_Is429WithRateLimitedMessage()
    {
        // given: email X существует; 3 запроса recovery/request за последний час.
        using var client = B17Host.CreateClient(_factory);
        B17Host.SeedStudent(_factory, Login, Email, FullName);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var response = await B17Host.PostJsonAsync(
                client, B17Host.RecoveryRequestEndpoint, RequestBody);
            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                $"Запрос {attempt}/3 (существующий email) ожидался 200 (лимит 3/час не исчерпан), "
                + $"фактически {(int)response.StatusCode}.");
        }

        // when: 4-й request на тот же email X.
        using var fourth = await B17Host.PostJsonAsync(
            client, B17Host.RecoveryRequestEndpoint, RequestBody);

        // then: 429; message «Слишком много попыток. Повторите позже».
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        var content = await fourth.Content.ReadAsStringAsync();
        var message = B17Host.ExtractEnvelopeMessage(content, "recovery/request 4-й (существующий email)");
        Assert.True(
            message.Equals(B17Host.RateLimitedMessage, StringComparison.Ordinal),
            $"Ожидался message «{B17Host.RateLimitedMessage}» дословно, фактически «{message}».");
    }
}
