using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-075 «recovery/confirm: пятая неверная аннулирует код» (boundary, FR-013, P0).
///
/// given: живой код у пользователя (DI-сид + POST /auth/recovery/request, код из
///        [DEV-EMAIL]-записи sink), доведён до attempts=4 четырьмя неверными
///        подтверждениями;
/// when:  пятая неверная попытка confirm {email, code:'000000'}; затем confirm
///        с верным кодом;
/// then:  пятая — 400 'Код восстановления не подходит'; код погашен (живых
///        кодов владельца больше нет — usedAt≠null); последующий верный код —
///        400 'Код восстановления не подходит'
///        (FR-013 AC «Пятая неверная аннулирует код»).
/// </summary>
public sealed class Ts075_RecoveryConfirmFifthWrongAnnulsTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "ts075";
    private const string Email = "ts075@example.com";
    private const string FullName = "Студент Семьдесят Пять";
    private const string WrongCode = "000000";
    private const int AttemptsBeforeAnnulment = 4;

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts075_RecoveryConfirmFifthWrongAnnulsTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FifthWrongConfirm_AnnulsCode_CorrectCodeIsRejectedAfterwards()
    {
        // given: живой код с attempts=4 (четыре неверных подтверждения).
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

        for (var attempt = 1; attempt <= AttemptsBeforeAnnulment; attempt++)
        {
            using var wrong = await B14RecoveryHarness.ConfirmAsync(client, Email, WrongCode);
            Assert.True(
                wrong.StatusCode == HttpStatusCode.BadRequest,
                $"Предусловие кейса: неверное подтверждение #{attempt} → 400, фактически " +
                $"{(int)wrong.StatusCode}: {await wrong.Content.ReadAsStringAsync()}");
            B14Assertions.MessageIs(
                await B14Assertions.ReadRootObjectAsync(wrong),
                B14RecoveryHarness.CodeRejectedMessage);
        }

        // when: пятая неверная попытка.
        using var fifth = await B14RecoveryHarness.ConfirmAsync(client, Email, WrongCode);

        // then: 400; message дословно.
        Assert.True(
            fifth.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 на пятой неверной попытке, фактически " +
            $"{(int)fifth.StatusCode}: {await fifth.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(fifth),
            B14RecoveryHarness.CodeRejectedMessage);

        // then: код погашен (usedAt≠null — живых кодов владельца больше нет).
        Assert.True(
            B14RecoveryCodeInspection.FindLiveRecoveryCode(_factory.Services, seeded.Id) is null,
            "Пятая неверная попытка обязана аннулировать код: живых кодов владельца быть не должно.");

        // then: последующий ВЕРНЫЙ код — 400 'Код восстановления не подходит'.
        using var correct = await B14RecoveryHarness.ConfirmAsync(client, Email, code);
        Assert.True(
            correct.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался 400 на верный код после аннулирования, фактически " +
            $"{(int)correct.StatusCode}: {await correct.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(correct),
            B14RecoveryHarness.CodeRejectedMessage);
    }
}
