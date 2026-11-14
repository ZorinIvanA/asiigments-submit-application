using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-085 «reset-password: сессии других пользователей не затрагиваются»
/// (data_integrity, FR-014, P2).
///
/// given: пользователь A с живым reset-токеном (recovery/request → confirm, код
///        из [DEV-EMAIL]-записи sink); пользователь B с активной сессией —
///        access-cookie (минт ITokenService, ADR-015) и действующий
///        refresh-токен (DI-сид ITokenService + IRefreshTokenRepository);
/// when:  A выполняет успешный reset-password;
/// then:  204; access B продолжает работать (GET /auth/me — 200); refresh B
///        валиден (POST /auth/refresh — 204)
///        (FR-014: «Сессии (access) других пользователей не затрагиваются»).
/// </summary>
public sealed class Ts085_ResetPasswordOtherSessionsUntouchedTests : IClassFixture<B15RecoveryWebAppFactory>
{
    private const string UserALogin = "ts085b15a";
    private const string UserAEmail = "ts085b15a@example.com";
    private const string UserAFullName = "Студент Восемьдесят Пять А";
    private const string UserBLogin = "ts085b15b";
    private const string UserBEmail = "ts085b15b@example.com";
    private const string UserBFullName = "Студент Восемьдесят Пять Б";

    private readonly B15RecoveryWebAppFactory _factory;

    public Ts085_ResetPasswordOtherSessionsUntouchedTests(B15RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ResetPassword_OfUserA_DoesNotTouchSessionsOfUserB()
    {
        // given: A с живым reset-токеном; B с активной сессией (access + refresh).
        _ = B15RecoveryApi.SeedUser(
            _factory,
            login: UserALogin,
            email: UserAEmail,
            fullName: UserAFullName,
            role: UserRoles.Student);
        var userB = B15RecoveryApi.SeedUser(
            _factory,
            login: UserBLogin,
            email: UserBEmail,
            fullName: UserBFullName,
            role: UserRoles.Student);

        using var sessionB = B15Harness.CreateSessionClient(_factory, userB.Id, UserRoles.Student);
        var refreshB = B15RecoveryApi.SeedRefreshToken(_factory, userB.Id);

        using var clientA = B15RecoveryApi.CreateClient(_factory);
        var resetTokenA = await RequestResetTokenAsync(clientA, UserAEmail);

        // when: A выполняет успешный сброс пароля.
        using var reset = await B15RecoveryApi.ResetPasswordAsync(
            clientA, resetTokenA, B15RecoveryApi.NewPassword, B15RecoveryApi.NewPassword);
        Assert.True(
            reset.StatusCode == HttpStatusCode.NoContent,
            $"Предусловие кейса: успешный сброс A → 204, фактически " +
            $"{(int)reset.StatusCode}: {await reset.Content.ReadAsStringAsync()}");

        // then: access B продолжает работать — GET /auth/me → 200.
        using var meB = await sessionB.GetAsync(B15RecoveryApi.MeEndpoint);
        Assert.True(
            meB.StatusCode == HttpStatusCode.OK,
            $"Сессия B не должна затрагиваться сбросом A: GET /auth/me → 200, фактически " +
            $"{(int)meB.StatusCode}: {await meB.Content.ReadAsStringAsync()}");

        // then: refresh B валиден — POST /auth/refresh с refresh-cookie B → 204.
        using var refreshClient = B15RecoveryApi.CreateClient(_factory);
        using var refreshBAfter = await B15RecoveryApi.RefreshAsync(refreshClient, refreshB);
        Assert.True(
            refreshBAfter.StatusCode == HttpStatusCode.NoContent,
            $"Refresh B должен остаться валидным: ожидался 204, фактически " +
            $"{(int)refreshBAfter.StatusCode}: {await refreshBAfter.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// Предусловие «живой reset-токен пользователя A»: recovery/request → 200,
    /// код из [DEV-EMAIL]-записи sink, confirm → 200 {resetToken}.
    /// </summary>
    private async Task<string> RequestResetTokenAsync(HttpClient client, string email)
    {
        using var request = await B15RecoveryApi.RequestRecoveryCodeAsync(client, email);
        Assert.True(
            request.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: POST /auth/recovery/request → 200, фактически " +
            $"{(int)request.StatusCode}: {await request.Content.ReadAsStringAsync()}");
        var code = B15RecoveryLogProbe.GetLastRecoveryCodeForEmail(_factory.LogSink, email);

        using var confirm = await B15RecoveryApi.ConfirmAsync(client, email, code);
        Assert.True(
            confirm.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: recovery/confirm живым кодом → 200, фактически " +
            $"{(int)confirm.StatusCode}: {await confirm.Content.ReadAsStringAsync()}");
        var confirmBody = await BodyAssertions.ReadRootObjectAsync(confirm);
        Assert.True(
            confirmBody.TryGetProperty("resetToken", out var resetTokenProperty)
            && resetTokenProperty.ValueKind == System.Text.Json.JsonValueKind.String
            && !string.IsNullOrWhiteSpace(resetTokenProperty.GetString()),
            "Предусловие кейса: confirm обязан выдать непустой resetToken.");
        return resetTokenProperty.GetString()!;
    }
}
