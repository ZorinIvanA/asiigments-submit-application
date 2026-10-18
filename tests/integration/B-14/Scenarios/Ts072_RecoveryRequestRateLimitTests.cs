using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-072 «recovery/request: лимит 3/час на email» (negative, FR-012/FR-004, P0).
///
/// given: 3 запроса recovery/request на email X за последний час (существующий и
///        несуществующий варианты — отдельные сценарии с РАЗНЫМИ ключами X:
///        лимитер ключуется lower(trim(email)), поэтому квота у каждого варианта
///        собственная в общем хосте фикстуры);
/// when:  4-й request на тот же email X;
/// then:  429; message 'Слишком много попыток. Повторите позже' — и для
///        существующего, и для несуществующего X (одинаковый статус и текст;
///        оракула существования нет; FR-012 AC «Лимит 3/час», FR-004: TryAcquire
///        ДО проверки существования).
/// </summary>
public sealed class Ts072_RecoveryRequestRateLimitTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string ExistingLogin = "ts072";
    private const string ExistingEmail = "ts072@example.com";
    private const string UnknownEmail = "nobody-ts072@example.com";
    private const string FullName = "Студент Семьдесят Два";

    /// <summary>Лимит recovery_request — 3 запроса в окно 3600 с (FR-004, IF-006).</summary>
    private const int RequestLimitPerHour = 3;

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts072_RecoveryRequestRateLimitTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FourthRequest_ForExistingEmail_IsRateLimited()
    {
        // given: существующий email; его квота не тронута.
        B14Harness.SeedUser(
            _factory,
            login: ExistingLogin,
            email: ExistingEmail,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var client = B14Harness.Create(_factory);

        // given: 3 запроса на email X — все 200.
        for (var attempt = 1; attempt <= RequestLimitPerHour; attempt++)
        {
            using var allowed = await B14RecoveryHarness.RequestRecoveryCodeAsync(client, ExistingEmail);
            Assert.True(
                allowed.StatusCode == HttpStatusCode.OK,
                $"Предусловие кейса: запрос #{attempt} в пределах лимита → 200, фактически " +
                $"{(int)allowed.StatusCode}: {await allowed.Content.ReadAsStringAsync()}");
        }

        // when: 4-й request на тот же email.
        using var fourth = await B14RecoveryHarness.RequestRecoveryCodeAsync(client, ExistingEmail);

        // then: 429 'Слишком много попыток. Повторите позже'.
        await AssertRateLimited(fourth, $"существующий {ExistingEmail}");
    }

    [Fact]
    public async Task FourthRequest_ForUnknownEmail_IsRateLimitedIdentically()
    {
        // given: несуществующий email — отдельный ключ лимитера, собственная квота.
        using var client = B14Harness.Create(_factory);
        for (var attempt = 1; attempt <= RequestLimitPerHour; attempt++)
        {
            using var allowed = await B14RecoveryHarness.RequestRecoveryCodeAsync(client, UnknownEmail);
            Assert.True(
                allowed.StatusCode == HttpStatusCode.OK,
                $"Предусловие кейса: запрос #{attempt} в пределах лимита → 200, фактически " +
                $"{(int)allowed.StatusCode}: {await allowed.Content.ReadAsStringAsync()}");
        }

        // when: 4-й request на тот же email.
        using var fourth = await B14RecoveryHarness.RequestRecoveryCodeAsync(client, UnknownEmail);

        // then: 429 с ТЕМ ЖЕ статусом и текстом, что и для существующего email
        // (оракула существования нет, FR-004/FR-012).
        await AssertRateLimited(fourth, $"несуществующий {UnknownEmail}");
    }

    /// <summary>
    /// Единый оракул обоих вариантов кейса: 429; message дословно
    /// 'Слишком много попыток. Повторите позже' (FR-023).
    /// </summary>
    private async Task AssertRateLimited(HttpResponseMessage response, string variant)
    {
        Assert.True(
            response.StatusCode == HttpStatusCode.TooManyRequests,
            $"Ожидался 429 на 4-й request ({variant}), фактически " +
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(response),
            "Слишком много попыток. Повторите позже");
    }
}
