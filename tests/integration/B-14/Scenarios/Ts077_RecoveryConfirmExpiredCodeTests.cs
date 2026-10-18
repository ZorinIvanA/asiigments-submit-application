using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-077 «recovery/confirm: просроченный код — 400» (boundary, FR-013, P1).
///
/// given: живой код у пользователя (DI-сид + POST /auth/recovery/request, код из
///        [DEV-EMAIL]-записи sink); инжектируемые часы (FR-003) переведены за
///        expiresAt — TTL кода 10 минут, время сдвинуто на 10 минут и 1 секунду;
/// when:  POST /api/v1/auth/recovery/confirm {email, code} этим кодом;
/// then:  400 'Код восстановления не подходит' (просроченный код = не живой;
///        FR-013).
/// </summary>
public sealed class Ts077_RecoveryConfirmExpiredCodeTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "ts077";
    private const string Email = "ts077@example.com";
    private const string FullName = "Студент Семьдесят Семь";

    /// <summary>TTL кода восстановления — 10 минут (IF-008); проверка — сразу за границей.</summary>
    private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(10);

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts077_RecoveryConfirmExpiredCodeTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Confirm_WithExpiredCode_IsRejected()
    {
        // given: живой код; часы — за expiresAt кода (TTL 10 минут).
        var seeded = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var client = B14Harness.Create(_factory);
        var code = await B14RecoveryHarness.RequestLiveCodeAsync(_factory, client, Email);
        _factory.Clock.Advance(CodeTtl.Add(TimeSpan.FromSeconds(1)));

        // when: подтверждение просроченным кодом.
        using var confirm = await B14RecoveryHarness.ConfirmAsync(client, Email, code);

        // then: 400 'Код восстановления не подходит' (просроченный = не живой).
        Assert.True(
            confirm.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 на просроченном коде, фактически " +
            $"{(int)confirm.StatusCode}: {await confirm.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(confirm),
            B14RecoveryHarness.CodeRejectedMessage);
    }
}
