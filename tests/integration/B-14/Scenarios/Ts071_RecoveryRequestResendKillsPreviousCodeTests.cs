using System.Text.Json;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-071 «recovery/request: переотправка гасит прежний код»
/// (data_integrity, FR-012/FR-013, P0).
///
/// given: живой код C1 пользователя student01@example.com (DI-сид + собственный
///        POST /auth/recovery/request, значение из [DEV-EMAIL]-записи sink);
/// when:  повторный POST recovery/request того же email; затем POST
///        /auth/recovery/confirm с кодом C1;
/// then:  повторный request — 200 (пустое тело); прежний код C1 погашен
///        (usedAt≠null: прямого шва чтения usedAt в IF-015 нет — C1 не живой,
///        живой код владельца теперь C2, подтверждение C1 отвергается как
///        использованное); новый код C2 жив (инспекция: usedAt=null, attempts=0);
///        подтверждение C1 — 400 'Код восстановления не подходит' (FR-012 AC
///        «Переотправка гасит прежний код»; одновременно жив максимум один код).
/// </summary>
public sealed class Ts071_RecoveryRequestResendKillsPreviousCodeTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Студент Первый";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts071_RecoveryRequestResendKillsPreviousCodeTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Resend_AnnulsPreviousCode_NewCodeIsTheOnlyLiveOne()
    {
        // given: живой код C1.
        var seeded = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var client = B14Harness.Create(_factory);
        var firstCode = await B14RecoveryHarness.RequestLiveCodeAsync(_factory, client, Email);

        // when: повторный request того же email.
        using var resend = await B14RecoveryHarness.RequestRecoveryCodeAsync(client, Email);

        // then: повторный request — 200 (пустое тело, ISS-014).
        Assert.True(
            resend.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 на повторном request, фактически " +
            $"{(int)resend.StatusCode}: {await resend.Content.ReadAsStringAsync()}");
        await B14RecoveryRequestProbe.AssertEmptyOkBodyAsync(resend, "TS-071");
        var resentCode = _factory.LogSink.GetLastRecoveryCodeForEmail(Email);
        Assert.NotEqual(firstCode, resentCode);

        // then: новый код C2 жив (usedAt=null, attempts=0); прежний C1 больше не
        // живой — живой код владельца теперь C2.
        var live = B14RecoveryCodeInspection.FindLiveRecoveryCode(_factory.Services, seeded.Id);
        Assert.True(
            live is not null,
            "После переотправки обязан быть живой новый код C2.");
        Assert.Equal(0, live!.Attempts);
        Assert.Null(live.UsedAt);

        // then: подтверждение C1 — 400 'Код восстановления не подходит'
        // (гашённый переотправкой код отвергается как использованный).
        using var oldConfirm = await B14RecoveryHarness.ConfirmAsync(client, Email, firstCode);
        Assert.True(
            oldConfirm.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался 400 на подтверждение прежнего кода C1, фактически " +
            $"{(int)oldConfirm.StatusCode}: {await oldConfirm.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(oldConfirm),
            B14RecoveryHarness.CodeRejectedMessage);

        // then: C2 — тот самый живой код: подтверждение C2 → 200 с resetToken
        // (FR-013 AC «Верный код» — не затронут гашением C1).
        using var newConfirm = await B14RecoveryHarness.ConfirmAsync(client, Email, resentCode);
        Assert.True(
            newConfirm.StatusCode == HttpStatusCode.OK,
            $"Ожидался 200 на подтверждение нового кода C2, фактически " +
            $"{(int)newConfirm.StatusCode}: {await newConfirm.Content.ReadAsStringAsync()}");
        var body = await B14Assertions.ReadRootObjectAsync(newConfirm);
        Assert.True(
            body.TryGetProperty("resetToken", out var resetToken)
            && resetToken.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(resetToken.GetString()),
            "Новый код C2 обязан подтверждаться выдачей непустого resetToken.");
    }
}
