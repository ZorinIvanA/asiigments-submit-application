using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-075 (P1, data_integrity; FR-014) «reset-password: просроченный токен
/// гасится».
/// given: reset-токен с expiresAt в прошлом (инжектируемые часы); шов хранилища
///        доступен.
/// when:  POST reset-password с этим токеном и валидными паролями.
/// then:  400 'Ссылка восстановления недействительна или истекла'; usedAt токена
///        проставлен (гашение найденного неживого). FR-014 AC «Просроченный токен
///        гасится».
/// </summary>
public sealed class Ts075_ResetPasswordExpiredTokenConsumedTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentEmail = "student01@example.com";
    private const string TokenValue = "ts075-expired-reset-token";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS075_ResetPassword_WithExpiredToken_IsRejectedAndTokenRecordIsConsumed()
    {
        // given: reset-токен с expiresAt в прошлом (сеется в момент T0 с TTL 0,
        // часы переводятся за границу срока); шов хранилища доступен.
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        var tokenHash = B13RecoverySeed.AddResetToken(_factory, user.Id, TokenValue, ttl: TimeSpan.Zero);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST reset-password с этим токеном и валидными паролями.
        using var reset = await B13RecoveryApi.ResetPasswordAsync(client, TokenValue, "NewPass1!", "NewPass1!");

        // then: 400 'Ссылка восстановления недействительна или истекла'.
        await ApiAssert.AssertMessageAsync(reset, HttpStatusCode.BadRequest, ErrorTexts.ResetTokenInvalid);

        // then: usedAt токена проставлен (найденный неживой токен гасится) —
        // инспекция записи по дайджесту через шов хранилища.
        var record = B13RecoverySeed.ResetTokenByHash(_factory, tokenHash);
        Assert.NotNull(record);
        Assert.NotNull(record.UsedAt);
    }
}
