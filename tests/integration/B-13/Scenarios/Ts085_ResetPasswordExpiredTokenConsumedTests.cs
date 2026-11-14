using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-085 (P1, negative; FR-014) «Reset-password: просроченный токен дополнительно
/// гасится».
/// given: токен с expiresAt в прошлом (часы переведены за TTL 15 минут, IF-003;
///        инжектируемые часы FR-003/ADR-002), usedAt=null.
/// when:  POST с этим токеном и валидными паролями.
/// then:  400 RESET_LINK_INVALID 'Ссылка восстановления недействительна или
///        истекла'; usedAt токена проставлен (инспекция хранилища)
///        (FR-014 AC «Просроченный токен гасится»).
/// </summary>
public sealed class Ts085_ResetPasswordExpiredTokenConsumedTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentEmail = "student01@example.com";
    private const string TokenValue = "ts085-expired-reset-token";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS085_ResetPassword_WithExpiredToken_IsRejectedAndTokenAdditionallyConsumed()
    {
        // given: живой при сиде токен (TTL 15 минут по умолчанию, usedAt=null);
        // часы переведены за expiresAt.
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        var tokenHash = B13RecoverySeed.AddResetToken(_factory, user.Id, TokenValue);
        _factory.Time.Advance(TimeSpan.FromMinutes(15).Add(TimeSpan.FromSeconds(1)));
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST с этим токеном и валидными паролями.
        using var reset = await B13RecoveryApi.ResetPasswordAsync(client, TokenValue, "NewPass1!", "NewPass1!");

        // then: 400 RESET_LINK_INVALID 'Ссылка восстановления недействительна
        // или истекла'.
        await ApiAssert.AssertMessageAsync(
            reset, HttpStatusCode.BadRequest, ErrorTexts.ResetTokenInvalid);

        // then: usedAt токена проставлен (найденный неживой токен ДОПОЛНИТЕЛЬНО
        // гасится — инспекция записи по дайджесту через шов хранилища).
        var record = B13RecoverySeed.ResetTokenByHash(_factory, tokenHash);
        Assert.NotNull(record);
        Assert.NotNull(record.UsedAt);
    }
}
