using LabsApp.IntegrationTests.B08.Limiters.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-021 «Recovery/request: 429 не раскрывает существование email»
/// (negative, FR-004 + FR-012, P0). «4-й» запрос на существующий email —
/// четвёртый ПО ЕГО СОБСТВЕННОМУ ключу (нормативный AC FR-004: «4-й запрос на
/// тот же email и 4-й на существующий student01@example.com» — оба 4-е
/// запроса своих ключей), поэтому given готовит по 3 метки каждому из двух
/// email; состояние «его лимит не исчерпан» — начальное состояние счётчика
/// существующего email до сценария.
///
/// given: выполнены 3 запроса POST /auth/recovery/request на
///        'unknown@example.com' за текущий час; пользователь
///        student01@example.com существует (лимит его ключа до сценария не
///        тронут); для 'student01@example.com' выполнены 3 запроса — его ключ
///        подготовлен к собственному 4-му запросу.
/// when:  4-й POST /auth/recovery/request на 'unknown@example.com'; затем 4-й
///        на 'student01@example.com'.
/// then:  оба — 429 RATE_LIMITED 'Слишком много попыток. Повторите позже':
///        одинаковые статус и сообщение для несуществующего и существующего
///        email, оракула существования нет (AC FR-004 «Recovery: 429 не
///        раскрывает существование email»).
/// </summary>
public sealed class Ts021_Recovery429HidesEmailExistenceTests : IClassFixture<B08LimitersWebAppFactory>
{
    private const string ExistingEmail = "student01@example.com";
    private const string UnknownEmail = "unknown@example.com";
    private const string RateLimitedMessage = "Слишком много попыток. Повторите позже";

    private readonly B08LimitersWebAppFactory _factory;

    public Ts021_Recovery429HidesEmailExistenceTests(B08LimitersWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RecoveryRequest_FourthRequests_429IdenticalForUnknownAndExistingEmail()
    {
        // given: student01@example.com существует (регистрация через публичный
        // API; его recovery-счётчик до сценария не тронут).
        using var seeded = await B08LimitersClients.RegisterStudentAsync(
            _factory, "Student One", "student01", ExistingEmail);
        using var client = B08LimitersClients.CreateClientWithoutCookies(_factory);

        // given: 3 запроса на несуществующий email за текущий час — до отказа
        // лимита ответы 200 (существование не раскрывается и до исчерпания).
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = await client.PostAsJsonAsync(
                B08LimitersClients.RecoveryRequestEndpoint, new { email = UnknownEmail });
            Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        }

        // given: 3 запроса на существующий email — его собственный ключ
        // подготовлен к 4-му запросу (до исчерпания — 200).
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = await client.PostAsJsonAsync(
                B08LimitersClients.RecoveryRequestEndpoint, new { email = ExistingEmail });
            Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        }

        // when: 4-й запрос на 'unknown@example.com'; затем 4-й на существующий
        // 'student01@example.com'.
        using var fourthUnknown = await client.PostAsJsonAsync(
            B08LimitersClients.RecoveryRequestEndpoint, new { email = UnknownEmail });
        using var fourthExisting = await client.PostAsJsonAsync(
            B08LimitersClients.RecoveryRequestEndpoint, new { email = ExistingEmail });

        // then: оба — 429 RATE_LIMITED с одинаковыми статусом и сообщением —
        // оракула существования email нет.
        Assert.Equal(HttpStatusCode.TooManyRequests, fourthUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, fourthExisting.StatusCode);
        var unknownEnvelope = await B08LimitersBodyAssertions.ReadRootObjectAsync(fourthUnknown);
        B08LimitersBodyAssertions.MessageIs(unknownEnvelope, RateLimitedMessage);
        var existingEnvelope = await B08LimitersBodyAssertions.ReadRootObjectAsync(fourthExisting);
        B08LimitersBodyAssertions.MessageIs(existingEnvelope, RateLimitedMessage);
    }
}
