using LabsApp.IntegrationTests.B08.Limiters.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// TS-022 «Матрица recovery_request: 3/3600с по lower(email), 429 без оракула
/// существования» (negative, FR-004 + FR-012, P0).
///
/// given: лимитер recovery пуст; пользователь student01@example.com существует
///        (регистрация через публичный API); пользователя unknown@example.com
///        нет.
/// when:  3 запроса POST /api/v1/auth/recovery/request на 'unknown@example.com'
///        (среди них вариант регистра 'Unknown@Example.com'), затем 4-й на него
///        же; отдельно 3 запроса на 'student01@example.com' и 4-й.
/// then:  оба 4-х запроса — 429 с одинаковым статусом и сообщением 'Слишком
///        много попыток. Повторите позже' для существующего и несуществующего
///        email; ключ — lower(trim(email)) (FR-004 AC «Recovery: 429 не
///        раскрывает существование»).
/// </summary>
public sealed class Ts022_RecoveryRequestLimitTests : IClassFixture<B08LimitersRateLimitWebAppFactory>
{
    private const string ExistingEmail = "student01@example.com";
    private const string UnknownEmail = "unknown@example.com";
    private const string RateLimitedMessage = "Слишком много попыток. Повторите позже";

    private readonly B08LimitersRateLimitWebAppFactory _factory;

    public Ts022_RecoveryRequestLimitTests(B08LimitersRateLimitWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RecoveryRequest_FourthRequest_429IdenticalForExistingAndUnknownEmail()
    {
        // given: student01@example.com существует; unknown@example.com нет;
        // лимитер recovery пуст (свежая фикстура).
        using var seeded = await B08LimitersClients.RegisterStudentAsync(
            _factory, "Student One", "student01", ExistingEmail);
        using var client = B08LimitersClients.CreateClientWithoutCookies(_factory);

        // when: 3 запроса на несуществующий email (среди них регистровый и
        // trim-варианты — тот же ключ lower(trim(email))).
        string[] unknownVariants = [UnknownEmail, "Unknown@Example.com", $" {UnknownEmail} "];
        foreach (var email in unknownVariants)
        {
            using var request = await client.PostAsJsonAsync(B08LimitersClients.RecoveryRequestEndpoint, new { email });

            // then (промежуточно): попытки до исчерпания лимита — 200 (оракула нет).
            Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        }

        // ... затем 4-й запрос на тот же email.
        using var fourthUnknown = await client.PostAsJsonAsync(
            B08LimitersClients.RecoveryRequestEndpoint, new { email = UnknownEmail });

        // when: отдельно 3 запроса на существующий email и 4-й.
        string[] existingVariants = [ExistingEmail, "STUDENT01@EXAMPLE.COM", $" {ExistingEmail} "];
        foreach (var email in existingVariants)
        {
            using var request = await client.PostAsJsonAsync(B08LimitersClients.RecoveryRequestEndpoint, new { email });
            Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        }

        using var fourthExisting = await client.PostAsJsonAsync(
            B08LimitersClients.RecoveryRequestEndpoint, new { email = ExistingEmail });

        // then: оба 4-х запроса — 429 с одинаковым статусом и сообщением
        // 'Слишком много попыток. Повторите позже' (429 не раскрывает
        // существование email).
        Assert.Equal(HttpStatusCode.TooManyRequests, fourthUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, fourthExisting.StatusCode);
        var unknownEnvelope = await B08LimitersBodyAssertions.ReadRootObjectAsync(fourthUnknown);
        B08LimitersBodyAssertions.MessageIs(unknownEnvelope, RateLimitedMessage);
        var existingEnvelope = await B08LimitersBodyAssertions.ReadRootObjectAsync(fourthExisting);
        B08LimitersBodyAssertions.MessageIs(existingEnvelope, RateLimitedMessage);
    }
}
