using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-089 «reset-password: сессии других пользователей не затрагиваются»
/// (data_integrity, FR-014, P1).
///
/// given: пользователь A выполняет сброс пароля (recovery/request → confirm,
///        код из [DEV-EMAIL]-записи sink); пользователь B имеет валидные
///        access- и refresh-cookie (access — минт ITokenService хоста;
///        refresh — ITokenService.CreateRefreshToken + ISecurityTokenRepository);
/// when:  сброс пароля A (204); затем GET /auth/me и POST /auth/refresh под B;
/// then:  оба запроса B — 200/204 (сессии других пользователей не затронуты;
///        FR-014).
/// </summary>
public sealed class Ts089_ResetPasswordOtherSessionsUntouchedTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string UserALogin = "ts089a";
    private const string UserAEmail = "ts089a@example.com";
    private const string UserAFullName = "Студент Восемьдесят Девять А";
    private const string UserBLogin = "ts089b";
    private const string UserBEmail = "ts089b@example.com";
    private const string UserBFullName = "Студент Восемьдесят Девять Б";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts089_ResetPasswordOtherSessionsUntouchedTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ResetPassword_OfUserA_KeepsAccessAndRefreshOfUserBValid()
    {
        // given: пользователь A с живым reset-токеном; пользователь B с валидными
        // access- и refresh-cookie.
        _ = B14Harness.SeedUser(
            _factory,
            login: UserALogin,
            email: UserAEmail,
            fullName: UserAFullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        var userB = B14Harness.SeedUser(
            _factory,
            login: UserBLogin,
            email: UserBEmail,
            fullName: UserBFullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var sessionB = B14Harness.CreateSessionClient(_factory, userB.Id, UserRoles.Student);
        var refreshB = B14Harness.SeedRefreshToken(_factory, userB.Id);

        using var clientA = B14Harness.Create(_factory);
        var code = await B14RecoveryHarness.RequestLiveCodeAsync(_factory, clientA, UserAEmail);
        using var confirm = await B14RecoveryHarness.ConfirmAsync(clientA, UserAEmail, code);
        Assert.True(
            confirm.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: recovery/confirm живым кодом → 200, фактически " +
            $"{(int)confirm.StatusCode}: {await confirm.Content.ReadAsStringAsync()}");
        var confirmBody = await B14Assertions.ReadRootObjectAsync(confirm);
        Assert.True(
            confirmBody.TryGetProperty("resetToken", out var resetTokenProperty)
            && resetTokenProperty.ValueKind == System.Text.Json.JsonValueKind.String
            && !string.IsNullOrWhiteSpace(resetTokenProperty.GetString()),
            "Предусловие кейса: confirm обязан выдать непустой resetToken.");
        var resetToken = resetTokenProperty.GetString()!;

        // when: сброс пароля A.
        using var reset = await B14RecoveryHarness.ResetPasswordAsync(
            clientA, resetToken, B14Harness.NewPassword, B14Harness.NewPassword);
        Assert.True(
            reset.StatusCode == HttpStatusCode.NoContent,
            $"Предусловие кейса: успешный сброс A → 204, фактически " +
            $"{(int)reset.StatusCode}: {await reset.Content.ReadAsStringAsync()}");

        // then: GET /auth/me под B — 200 (access-cookie B не затронут).
        using var meB = await sessionB.GetAsync(B14RecoveryHarness.MeEndpoint);
        Assert.True(
            meB.StatusCode == HttpStatusCode.OK,
            $"Сессия B не должна затрагиваться сбросом A: GET /auth/me → 200, фактически " +
            $"{(int)meB.StatusCode}: {await meB.Content.ReadAsStringAsync()}");

        // then: POST /auth/refresh под B (refresh-cookie B) — 204.
        using var refreshBAfter = await B14Harness.RefreshAsync(B14Harness.Create(_factory), refreshB);
        Assert.True(
            refreshBAfter.StatusCode == HttpStatusCode.NoContent,
            $"Refresh-cookie B должна остаться валидной: ожидался 204, фактически " +
            $"{(int)refreshBAfter.StatusCode}: {await refreshBAfter.Content.ReadAsStringAsync()}");
    }
}
