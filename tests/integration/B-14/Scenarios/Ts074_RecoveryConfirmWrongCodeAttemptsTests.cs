using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-074 «recovery/confirm: неверный код — attempts инкрементируется»
/// (negative, FR-013, P0).
///
/// given: живой код у пользователя (DI-сид + POST /auth/recovery/request, код из
///        [DEV-EMAIL]-записи sink), attempts=0;
/// when:  POST /api/v1/auth/recovery/confirm {email, code:'000000'};
/// then:  400; message 'Код восстановления не подходит'; у живого кода
///        attempts=1 (инспекция хранилища, IF-015), код остался живым
///        (usedAt=null — и верный код по-прежнему подтверждается 200)
///        (FR-013 AC «Неверный код — попытки считаются»).
/// </summary>
public sealed class Ts074_RecoveryConfirmWrongCodeAttemptsTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "ts074";
    private const string Email = "ts074@example.com";
    private const string FullName = "Студент Семьдесят Четыре";
    private const string WrongCode = "000000";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts074_RecoveryConfirmWrongCodeAttemptsTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Confirm_WithWrongCode_IncrementsAttempts_AndKeepsCodeAlive()
    {
        // given: живой код пользователя (attempts=0).
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
        Assert.NotEqual(WrongCode, code);

        // when: подтверждение неверным кодом.
        using var wrong = await B14RecoveryHarness.ConfirmAsync(client, Email, WrongCode);

        // then: 400; message дословно.
        Assert.True(
            wrong.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)wrong.StatusCode}: {await wrong.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(wrong),
            B14RecoveryHarness.CodeRejectedMessage);

        // then: attempts=1; код остался живым (usedAt=null).
        var live = B14RecoveryCodeInspection.FindLiveRecoveryCode(_factory.Services, seeded.Id);
        Assert.True(
            live is not null,
            "После одной неверной попытки код обязан остаться живым (usedAt=null, expiresAt>now).");
        Assert.True(
            live!.Attempts == 1,
            $"Ожидался attempts=1 после первой неверной попытки, фактически {live.Attempts}.");
        Assert.Null(live.UsedAt);

        // then: код остался живым — верный код по-прежнему подтверждается.
        using var correct = await B14RecoveryHarness.ConfirmAsync(client, Email, code);
        Assert.True(
            correct.StatusCode == HttpStatusCode.OK,
            $"Живой код после одной неверной попытки обязан подтвердиться 200, фактически " +
            $"{(int)correct.StatusCode}: {await correct.Content.ReadAsStringAsync()}");
    }
}
