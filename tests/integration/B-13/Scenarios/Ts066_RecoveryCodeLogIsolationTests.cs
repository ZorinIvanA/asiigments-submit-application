using LabsApp.Auth;
using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Observability;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-066 (P0, nfr; FR-012/NFR-006) «recovery/request: изоляция кода в логах —
/// Development и Production».
/// given: тестовый log-sink разделяет категории; две конфигурации хоста:
///        Development и Production (заданы Auth__JwtKey и нестандартный
///        Seed__TeacherPassword).
/// when:  POST recovery/request на существующий email в обеих конфигурациях;
///        сопоставление кода из записи 'EmailDev' со всеми записями sink.
/// then:  Development — ни одна запись вне категории 'EmailDev' не содержит код;
///        Production — ни одна запись вообще не содержит код (одно warning-
///        сообщение no-op без адресата и содержимого). FR-012 AC «Код не попадает
///        в основной лог» (ISS-003/SEC-002); NFR-006.
/// </summary>
public sealed class Ts066_RecoveryCodeLogIsolationTests(
    B13RecoveryDevSpyHost devFactory,
    B13RecoveryProdSecretsHost prodFactory)
    : IClassFixture<B13RecoveryDevSpyHost>, IClassFixture<B13RecoveryProdSecretsHost>
{
    private const string DevEmail = "student66dev@example.com";
    private const string ProdEmail = "student66prod@example.com";

    private readonly B13RecoveryDevSpyHost _devFactory = devFactory;
    private readonly B13RecoveryProdSecretsHost _prodFactory = prodFactory;

    [Fact]
    public async Task TS066_RecoveryRequest_CodeIsolatedToEmailDevInDevelopmentAndAbsentInProduction()
    {
        // ---------- Development ----------
        // given: Development-хост с log-sink; существующий email (DI-сид).
        var devUser = B13RecoverySeed.AddStudent(_devFactory, "student66dev", DevEmail);
        var devTokens = _devFactory.Services.GetRequiredService<ITokenService>();
        var devSecurity = _devFactory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var devClient = B13RecoveryHarness.CreateClient(_devFactory);

        // when: POST recovery/request на существующий email.
        using var devResponse = await B13RecoveryApi.RecoveryRequestAsync(devClient, DevEmail);
        await B13RecoveryApi.AssertEmptyBodyOkAsync(devResponse);

        // then: код из записи 'EmailDev' (опознан верификацией по хэшу живого кода)
        // отсутствует в КАЖДОЙ записи вне категории 'EmailDev'.
        var devLive = devSecurity.FindLiveForUser(devUser.Id);
        Assert.NotNull(devLive);
        var devEntries = B13RecoveryLogAsserts.DevEmailEntries(_devFactory.LogSink);
        var devEmailEntry = Assert.Single(devEntries);
        B13RecoveryLogAsserts.AssertDevEmailFormat(devEmailEntry);
        var devCode = B13RecoveryLogAsserts.VerifiedCode(devEmailEntry, devTokens, devLive.CodeHash);
        B13RecoveryLogAsserts.AssertNoEntryContains(_devFactory.LogSink, devCode, B13RecoveryLogAsserts.EmailDevCategory);

        // ---------- Production ----------
        // given: Production-хост (Auth__JwtKey задан, Seed__TeacherPassword
        // нестандартный — guard пройден); log-sink; существующий email (DI-сид).
        var prodUser = B13RecoverySeed.AddStudent(_prodFactory, "student66prod", ProdEmail);
        var prodSecurity = _prodFactory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var prodClient = B13RecoveryHarness.CreateClient(_prodFactory);

        // when: POST recovery/request на существующий email.
        using var prodResponse = await B13RecoveryApi.RecoveryRequestAsync(prodClient, ProdEmail);
        await B13RecoveryApi.AssertEmptyBodyOkAsync(prodResponse);

        // then: ни одна запись журнала вообще не содержит код: ни одна запись не
        // содержит адресата, категория 'EmailDev' отсутствует, ни один 6-цифровой
        // кандидат ни в одной записи не верифицируется как код хранилища.
        var prodLive = prodSecurity.FindLiveForUser(prodUser.Id);
        Assert.NotNull(prodLive);
        Assert.Empty(B13RecoveryLogAsserts.DevEmailEntries(_prodFactory.LogSink));
        foreach (var entry in _prodFactory.LogSink.Snapshot())
        {
            Assert.DoesNotContain(ProdEmail, entry.Message, StringComparison.Ordinal);
        }

        foreach (var candidate in B13RecoveryLogAsserts.AllSixDigitCandidates(_prodFactory.LogSink))
        {
            Assert.False(
                _prodFactory.Services.GetRequiredService<ITokenService>()
                    .VerifyRecoveryCode(candidate, prodLive.CodeHash),
                $"Кандидат «{candidate}» из записи журнала верифицировался как код восстановления " +
                "в Production — NFR-006 нарушен.");
        }

        // then: ровно ОДНО warning-сообщение no-op ОТПРАВКИ — без адресата и
        // содержимого (категория Hosting.Configuration, ProductionEmailSender
        // IF-005; прочие warning хоста — инфраструктурный шум вне кейса).
        var deliveryWarnings = _prodFactory.LogSink.Snapshot()
            .Where(entry => entry.Level == LogLevel.Warning
                && entry.Category == ProductionEmailSender.LogCategory)
            .ToArray();
        var noopWarning = Assert.Single(deliveryWarnings);
        Assert.DoesNotContain("@", noopWarning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("body=", noopWarning.Message, StringComparison.Ordinal);
    }
}
