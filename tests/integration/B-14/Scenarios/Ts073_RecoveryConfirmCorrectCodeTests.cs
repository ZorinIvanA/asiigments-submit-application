using System.Text.Json;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-073 «recovery/confirm: верный код выдаёт resetToken» (happy_path, FR-013, P0).
///
/// given: студент student01@example.com создан DI-сидом (B14Harness.SeedUser,
///        реальный IPasswordHasher хоста); живой код получен собственным
///        POST /auth/recovery/request и извлечён из [DEV-EMAIL]-записи тестового
///        sink (IF-005); точка отсчёта счётчика KDF снята (Δkdf — IF-002);
/// when:  POST /api/v1/auth/recovery/confirm {email:'student01@example.com', code}
///        верным кодом;
/// then:  200; тело {resetToken: непустая строка ≥256 бит — ≥43 символа
///        base64url-строки CSPRNG}; код погашен (живых кодов у владельца больше
///        нет — usedAt≠null); PasswordResetToken создан с TTL ровно 15 минут
///        (после перевода инжектируемых часов точно на границу TTL сброс по
///        токену отклонён 400 RESET_LINK_INVALID); Δkdf=0 (FR-013 AC «Верный
///        код»; ASM-005: коды/токены — быстрые SHA-256, KDF не выполняют).
/// </summary>
public sealed class Ts073_RecoveryConfirmCorrectCodeTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Студент Первый";

    /// <summary>Минимальная длина строки секрета ≥256 бит (base64url 32 байт = 43 симв.; IF-003).</summary>
    private const int ResetTokenMinLength = 43;

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts073_RecoveryConfirmCorrectCodeTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Confirm_WithCorrectCode_ReturnsResetToken_ConsumesCode_ZeroKdf()
    {
        // given: пользователь, живой код из [DEV-EMAIL]-записи, точка отсчёта KDF.
        var seeded = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        var kdfBefore = B14KdfProbe.Snapshot(_factory.Services);
        using var client = B14Harness.Create(_factory);
        var code = await B14RecoveryHarness.RequestLiveCodeAsync(_factory, client, Email);

        // when: подтверждение верным кодом.
        using var confirm = await B14RecoveryHarness.ConfirmAsync(client, Email, code);

        // then: 200; тело {resetToken: непустая строка ≥256 бит}.
        Assert.True(
            confirm.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)confirm.StatusCode}: {await confirm.Content.ReadAsStringAsync()}");
        var body = await B14Assertions.ReadRootObjectAsync(confirm);
        Assert.True(
            body.TryGetProperty("resetToken", out var resetTokenProperty),
            "В теле 200 отсутствует ключ resetToken.");
        Assert.Equal(JsonValueKind.String, resetTokenProperty.ValueKind);
        var resetToken = resetTokenProperty.GetString();
        Assert.False(
            string.IsNullOrWhiteSpace(resetToken),
            "resetToken — непустая строка (FR-013 AC «Верный код»: resetToken≠null).");
        Assert.True(
            resetToken!.Length >= ResetTokenMinLength,
            $"resetToken должен быть ≥256 бит (≥{ResetTokenMinLength} симв. base64url), " +
            $"фактически {resetToken.Length} символов.");

        // then: код погашен — живых кодов владельца больше нет (usedAt=now).
        Assert.True(
            B14RecoveryCodeInspection.FindLiveRecoveryCode(_factory.Services, seeded.Id) is null,
            "Код обязан быть погашен успешным подтверждением: живых кодов владельца быть не должно.");

        // then: PasswordResetToken жив ровно TTL 15 минут — на границе TTL (now+15 мин,
        // liveness строгая: expiresAt>now) сброс по токену уже отклонён.
        _factory.Clock.Advance(TimeSpan.FromMinutes(15));
        using var expiredReset = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, B14Harness.NewPassword, B14Harness.NewPassword);
        Assert.True(
            expiredReset.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался 400 на сброс по токену на границе TTL 15 минут, фактически " +
            $"{(int)expiredReset.StatusCode}: {await expiredReset.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(expiredReset),
            B14RecoveryHarness.ResetLinkInvalidMessage);

        // then: Δkdf=0 на request/confirm (и на отклонённом по токену reset — ASM-005/IF-008).
        var kdfAfter = B14KdfProbe.Snapshot(_factory.Services);
        Assert.True(
            B14KdfProbe.TotalDelta(kdfBefore, kdfAfter) == 0,
            $"Ожидался Δkdf=0 (FR-013/ASM-005), фактически {B14KdfProbe.TotalDelta(kdfBefore, kdfAfter)}.");
    }
}
