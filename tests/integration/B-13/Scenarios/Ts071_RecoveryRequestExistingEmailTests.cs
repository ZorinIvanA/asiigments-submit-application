using LabsApp.Auth;
using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-071 (P0, happy_path; FR-012/NFR-006) «recovery/request: существующий email —
/// 200 с пустым телом, один живой код, письмо в EmailDev».
/// given: Development; student01@example.com зарегистрирован, кодов нет; тестовый
///        log-sink с разделением категорий; часы T0; счётчик KDF обнулён
///        (дельты снимков, IF-002/ADR-031); IEmailSender — спай-декоратор
///        реальной dev-заглушки.
/// when:  POST /api/v1/auth/recovery/request {email:'student01@example.com'}.
/// then:  200; тело 0 байт (Content-Length: 0, НЕ '{}'); создан ровно один живой
///        RecoveryCode с expiresAt=T0+10 мин; IEmailSender вызван один раз;
///        6-значный код присутствует только в записи категории 'EmailDev'
///        (маркер [DEV-EMAIL]); Δkdf=0. FR-012 AC «Существующий email».
/// </summary>
public sealed class Ts071_RecoveryRequestExistingEmailTests(B13RecoveryDevSpyHost factory)
    : IClassFixture<B13RecoveryDevSpyHost>
{
    private const string StudentEmail = "student01@example.com";

    private readonly B13RecoveryDevSpyHost _factory = factory;

    [Fact]
    public async Task TS071_Request_ForExistingEmail_ReturnsEmptyBodyOneLiveCodeAndLetterInEmailDev()
    {
        // given: student01@example.com зарегистрирован (DI-сид), кодов нет
        // (свежий хост), счётчик KDF обнулён дельтой снимков.
        var user = B13RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        var services = _factory.Services;
        var tokens = services.GetRequiredService<ITokenService>();
        var securityTokens = services.GetRequiredService<ISecurityTokenRepository>();
        Assert.Null(securityTokens.FindLiveForUser(user.Id));
        var kdfBefore = B13RecoveryHarness.KdfSnapshot(_factory);

        // given: часы T0 (инжектируемые, FR-003); клиент без cookie-контейнера.
        var t0 = _factory.Time.GetUtcNow();
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST /api/v1/auth/recovery/request {email:'student01@example.com'}.
        using var response = await B13RecoveryApi.RecoveryRequestAsync(client, StudentEmail);

        // then: 200; тело 0 байт (Content-Length: 0, НЕ '{}') — ISS-014.
        await B13RecoveryApi.AssertEmptyBodyOkAsync(response);

        // then: создан ровно ОДИН живой RecoveryCode с expiresAt=T0+10 мин.
        var live = securityTokens.FindLiveForUser(user.Id);
        Assert.NotNull(live);
        Assert.Null(live.UsedAt);
        Assert.Equal(0, live.Attempts);
        Assert.Equal(t0.AddMinutes(10).UtcDateTime, live.ExpiresAt);
        var allCodes = B13RecoverySeed.AllRecoveryCodes(_factory);
        Assert.Single(allCodes);

        // then: IEmailSender вызван один раз.
        Assert.Equal(1, _factory.EmailSpy.Calls);

        // then: 6-значный код присутствует только в записи категории 'EmailDev'
        // (маркер [DEV-EMAIL]): запись единственна, код (ровно 6 ASCII-цифр)
        // верифицируется по хэшу хранилища и не встречается вне 'EmailDev'.
        var devEmailEntries = B13RecoveryLogAsserts.DevEmailEntries(_factory.LogSink);
        var devEmail = Assert.Single(devEmailEntries);
        var format = B13RecoveryLogAsserts.AssertDevEmailFormat(devEmail);
        Assert.Equal(StudentEmail, format.Groups["to"].Value);
        var code = B13RecoveryLogAsserts.VerifiedCode(devEmail, tokens, live.CodeHash);
        Assert.Matches("^[0-9]{6}$", code);
        B13RecoveryLogAsserts.AssertNoEntryContains(
            _factory.LogSink, code, B13RecoveryLogAsserts.EmailDevCategory);

        // then: Δkdf=0 (FR-012: операций KDF в эндпойнте — 0).
        var kdfAfter = B13RecoveryHarness.KdfSnapshot(_factory);
        Assert.Equal(0L, B13RecoveryKdf.TotalDelta(kdfBefore, kdfAfter));
    }
}
