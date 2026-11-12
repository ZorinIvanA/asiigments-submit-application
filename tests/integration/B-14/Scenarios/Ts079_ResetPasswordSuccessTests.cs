using System.Text.Json;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-079 «reset-password: успешный сброс» (happy_path, FR-014, P0).
///
/// given: студент student01 (student01@example.com, пароль 'Passw0rd!') DI-сидом;
///        у него активный refresh-токен (B14Harness.SeedRefreshToken); живой
///        resetToken получен через recovery/request → confirm (код из
///        [DEV-EMAIL]-записи sink); точка отсчёта счётчика KDF снята;
/// when:  POST /api/v1/auth/reset-password {resetToken, password:'NewPass1!',
///        confirmPassword:'NewPass1!'};
/// then:  204 с ПУСТЫМ телом (строго 204 — арбитраж ISS-004/ASM-016);
///        Δkdf(reset_password)=1 и Δkdf суммарно=1 (KDF только после предъявления
///        живого токена, IF-008); вход старым паролем — 401, новым — 200;
///        reset-токены пользователя погашены (повторное применение выданного
///        токена — 400 RESET_LINK_INVALID); refresh-токены отозваны
///        (POST /auth/refresh с прежним refresh-cookie — 401)
///        (FR-014 AC «Успешный сброс»).
/// </summary>
public sealed class Ts079_ResetPasswordSuccessTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Студент Первый";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts079_ResetPasswordSuccessTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ResetPassword_WithLiveToken_ResetsPassword_RevokesRefresh_ConsumesResetTokens()
    {
        // given: студент с активным refresh-токеном; живой resetToken; KDF-точка отсчёта.
        var seeded = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        var activeRefresh = B14Harness.SeedRefreshToken(_factory, seeded.Id);
        using var client = B14Harness.Create(_factory);
        var code = await B14RecoveryHarness.RequestLiveCodeAsync(_factory, client, Email);
        using var confirm = await B14RecoveryHarness.ConfirmAsync(client, Email, code);
        Assert.True(
            confirm.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: recovery/confirm живым кодом → 200, фактически " +
            $"{(int)confirm.StatusCode}: {await confirm.Content.ReadAsStringAsync()}");
        var confirmBody = await B14Assertions.ReadRootObjectAsync(confirm);
        Assert.True(
            confirmBody.TryGetProperty("resetToken", out var resetTokenProperty)
            && resetTokenProperty.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(resetTokenProperty.GetString()),
            "Предусловие кейса: confirm обязан выдать непустой resetToken.");
        var resetToken = resetTokenProperty.GetString()!;

        var kdfBefore = B14KdfProbe.Snapshot(_factory.Services);

        // when: сброс пароля живым resetToken.
        using var reset = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, B14Harness.NewPassword, B14Harness.NewPassword);

        // then: строго 204 с пустым телом (арбитраж ISS-004/ASM-016).
        Assert.True(
            reset.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204 (арбитраж ISS-004), фактически {(int)reset.StatusCode}: " +
            $"{await reset.Content.ReadAsStringAsync()}");
        var resetBody = await reset.Content.ReadAsStringAsync();
        Assert.True(
            string.IsNullOrEmpty(resetBody),
            $"Тело 204 обязано быть пустым (0 байт), фактически «{resetBody}».");

        // then: Δkdf(reset_password)=1; суммарно за участок — тоже 1 (IF-008:
        // KDF только после предъявления живого токена).
        var kdfAfterReset = B14KdfProbe.Snapshot(_factory.Services);
        Assert.True(
            B14KdfProbe.CallerDelta(kdfBefore, kdfAfterReset, "reset_password") == 1,
            "Ожидался Δkdf(reset_password)=1 (FR-014 AC «Успешный сброс»).");
        Assert.True(
            B14KdfProbe.TotalDelta(kdfBefore, kdfAfterReset) == 1,
            "Ожидался суммарный Δkdf=1 на успешном сбросе (IF-008).");

        // then: вход со старым паролем — 401, с новым — 200.
        using var oldPasswordLogin = await B14Harness.LoginAsync(
            B14Harness.Create(_factory), Login, B14Harness.TestUserPassword);
        Assert.True(
            oldPasswordLogin.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался 401 на вход старым паролем, фактически " +
            $"{(int)oldPasswordLogin.StatusCode}: {await oldPasswordLogin.Content.ReadAsStringAsync()}");
        using var newPasswordLogin = await B14Harness.LoginAsync(
            B14Harness.Create(_factory), Login, B14Harness.NewPassword);
        Assert.True(
            newPasswordLogin.StatusCode == HttpStatusCode.OK,
            $"Ожидался 200 на вход новым паролем, фактически " +
            $"{(int)newPasswordLogin.StatusCode}: {await newPasswordLogin.Content.ReadAsStringAsync()}");

        // then: refresh-токены пользователя отозваны — POST /auth/refresh с прежним
        // refresh-cookie → 401.
        using var refreshAfter = await B14Harness.RefreshAsync(B14Harness.Create(_factory), activeRefresh);
        Assert.True(
            refreshAfter.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался 401 на refresh отозванным токеном, фактически " +
            $"{(int)refreshAfter.StatusCode}: {await refreshAfter.Content.ReadAsStringAsync()}");

        // then: ВСЕ reset-токены пользователя погашены — повторное применение
        // выданного токена → 400 RESET_LINK_INVALID (одноразовость).
        using var reuse = await B14RecoveryHarness.ResetPasswordAsync(
            client, resetToken, B14Harness.NewPassword, B14Harness.NewPassword);
        Assert.True(
            reuse.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался 400 на повторное применение reset-токена, фактически " +
            $"{(int)reuse.StatusCode}: {await reuse.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(reuse),
            B14RecoveryHarness.ResetLinkInvalidMessage);
    }
}
