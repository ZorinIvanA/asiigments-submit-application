using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-207 «recovery/confirm: email и code триммятся» (boundary, FR-013, P2).
///
/// given: студент student01@example.com создан DI-сидом; живой код получен
///        POST /auth/recovery/request и извлечён из [DEV-EMAIL]-записи
///        тестового sink (IF-005); точка отсчёта счётчика KDF снята (IF-002);
/// when:  POST /api/v1/auth/recovery/confirm {email:'  student01@example.com  ',
///        code:' {код} '} — значения с ведущими/хвостовыми пробелами;
/// then:  200; тело {resetToken: непустая строка} — триммированные значения
///        совпали с ключом пользователя и хэшем кода; код погашен
///        (usedAt≠null — живых кодов владельца больше нет); Δkdf=0
///        (FR-013: «email и code триммятся»; ASM-005).
/// </summary>
public sealed class Ts207_RecoveryConfirmTrimsEmailAndCodeTests : IClassFixture<B15RecoveryWebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Студент Первый";

    private readonly B15RecoveryWebAppFactory _factory;

    public Ts207_RecoveryConfirmTrimsEmailAndCodeTests(B15RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Confirm_WithUntrimmedEmailAndCode_TrimmsAndConfirms()
    {
        // given: пользователь, живой код, точка отсчёта KDF.
        var seeded = B15Harness.SeedStudent(
            _factory,
            fullName: FullName,
            login: Login,
            email: Email);
        using var client = B15RecoveryApi.CreateClient(_factory);

        using var request = await B15RecoveryApi.RequestRecoveryCodeAsync(client, Email);
        Assert.True(
            request.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: POST /auth/recovery/request → 200, фактически " +
            $"{(int)request.StatusCode}: {await request.Content.ReadAsStringAsync()}");
        var code = B15RecoveryLogProbe.GetLastRecoveryCodeForEmail(_factory.LogSink, Email);

        var kdfBefore = B15KdfProbe.Snapshot(_factory.Services);

        // when: confirm с нетриммированными email и code.
        using var confirm = await B15RecoveryApi.ConfirmAsync(
            client,
            $"  {Email}  ",
            $" {code} ");

        // then: 200; тело {resetToken: непустая строка} — триммированные значения
        //       совпали с ключом пользователя и хэшем кода.
        Assert.True(
            confirm.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 на триммированных значениях, фактически " +
            $"{(int)confirm.StatusCode}: {await confirm.Content.ReadAsStringAsync()}");
        var body = await BodyAssertions.ReadRootObjectAsync(confirm);
        Assert.True(
            body.TryGetProperty("resetToken", out var resetTokenProperty)
            && resetTokenProperty.ValueKind == System.Text.Json.JsonValueKind.String
            && !string.IsNullOrWhiteSpace(resetTokenProperty.GetString()),
            "Ожидался непустой resetToken в теле 200 (FR-013: email и code триммятся).");

        // then: код погашен (usedAt≠null) — в пределах TTL 10 минут живых кодов
        //       владельца быть не должно (инспекция хранилища, IF-015).
        Assert.True(
            B15RecoveryCodeInspection.FindLiveRecoveryCode(_factory.Services, seeded.Id) is null,
            "Код обязан быть погашен успешным подтверждением: живых кодов владельца быть не должно.");

        // then: Δkdf=0 (ASM-005: recovery-эндпойнты без операций KDF).
        var kdfAfter = B15KdfProbe.Snapshot(_factory.Services);
        Assert.True(
            B15KdfProbe.TotalDelta(kdfBefore, kdfAfter) == 0,
            $"Ожидался Δkdf=0 (FR-013/ASM-005), фактически {B15KdfProbe.TotalDelta(kdfBefore, kdfAfter)}.");
    }
}
