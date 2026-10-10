using LabsApp.Auth;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-068 (P1, boundary; FR-012/FR-013) «recovery: истёкший через 10 минут код
/// отвергается».
/// given: живой код создан в момент T0 (известен из 'EmailDev'); инжектируемые
///        часы переведены на T0+10мин.
/// when:  POST /auth/recovery/confirm с этим кодом.
/// then:  400 'Код восстановления не подходит' (просроченный код = не живой;
///        TTL 10 минут). FR-013.
/// </summary>
public sealed class Ts068_RecoveryExpiredCodeTests(B13RecoveryDevSpyHost factory)
    : IClassFixture<B13RecoveryDevSpyHost>
{
    private const string StudentEmail = "student01@example.com";

    private readonly B13RecoveryDevSpyHost _factory = factory;

    [Fact]
    public async Task TS068_Confirm_WithCodeExpiredAtExactlyTtlBoundary_IsRejected()
    {
        // given: живой код создан в момент T0 (запрос recovery/request по
        // инжектируемым часам), значение — из записи 'EmailDev'.
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        var tokens = _factory.Services.GetRequiredService<ITokenService>();
        var securityTokens = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var client = B13RecoveryHarness.CreateClient(_factory);

        var t0 = _factory.Time.GetUtcNow();
        using var request = await B13RecoveryApi.RecoveryRequestAsync(client, StudentEmail);
        await B13RecoveryApi.AssertEmptyBodyOkAsync(request);

        var live = securityTokens.FindLiveForUser(user.Id);
        Assert.NotNull(live);
        Assert.Equal(t0.AddMinutes(10).UtcDateTime, live.ExpiresAt);
        var devEmail = Assert.Single(B13RecoveryLogAsserts.DevEmailEntries(_factory.LogSink));
        var code = B13RecoveryLogAsserts.VerifiedCode(devEmail, tokens, live.CodeHash);

        // when: часы переведены на T0+10мин (граница TTL: expiresAt > now ложно).
        _factory.Time.SetUtcNow(t0.AddMinutes(10));
        using var confirm = await B13RecoveryApi.ConfirmAsync(client, StudentEmail, code);

        // then: 400 'Код восстановления не подходит' (просроченный код = не живой).
        await ApiAssert.AssertMessageAsync(confirm, HttpStatusCode.BadRequest, ErrorTexts.RecoveryCodeRejected);
    }
}
