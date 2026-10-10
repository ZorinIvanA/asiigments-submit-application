using LabsApp.Auth;
using LabsApp.IntegrationTests.B11.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using LabsApp.Observability;
using LabsApp.Storage;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B11.Scenarios;

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
    B11RecoveryDevSpyHost devFactory,
    B11RecoveryProdSecretsHost prodFactory)
    : IClassFixture<B11RecoveryDevSpyHost>, IClassFixture<B11RecoveryProdSecretsHost>
{
    private const string DevEmail = "student66dev@example.com";
    private const string ProdEmail = "student66prod@example.com";

    private readonly B11RecoveryDevSpyHost _devFactory = devFactory;
    private readonly B11RecoveryProdSecretsHost _prodFactory = prodFactory;

    [Fact]
    public async Task TS066_RecoveryRequest_CodeIsolatedToEmailDevInDevelopmentAndAbsentInProduction()
    {
        // ---------- Development ----------
        // given: Development-хост с log-sink; существующий email (DI-сид).
        var devUser = B11RecoverySeed.AddStudent(_devFactory, "student66dev", DevEmail);
        var devTokens = _devFactory.Services.GetRequiredService<ITokenService>();
        var devSecurity = _devFactory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var devClient = HostClients.Create(_devFactory);

        // when: POST recovery/request на существующий email.
        using var devResponse = await B11RecoveryApi.RecoveryRequestAsync(devClient, DevEmail);
        await B11RecoveryApi.AssertEmptyBodyOkAsync(devResponse);

        // then: код из записи 'EmailDev' (опознан верификацией по хэшу живого кода)
        // отсутствует в КАЖДОЙ записи вне категории 'EmailDev'.
        var devLive = devSecurity.FindLiveForUser(devUser.Id);
        Assert.NotNull(devLive);
        var devEntries = B11RecoveryLogs.DevEmailEntries(_devFactory.LogSink);
        var devEmailEntry = Assert.Single(devEntries);
        B11RecoveryLogs.AssertDevEmailFormat(devEmailEntry);
        var devCode = B11RecoveryLogs.VerifiedCode(devEmailEntry, devTokens, devLive.CodeHash);
        B11RecoveryLogs.AssertNoEntryContains(_devFactory.LogSink, devCode, B11RecoveryLogs.EmailDevCategory);

        // ---------- Production ----------
        // given: Production-хост (Auth__JwtKey задан, Seed__TeacherPassword
        // нестандартный — guard пройден); log-sink; существующий email (DI-сид).
        var prodUser = B11RecoverySeed.AddStudent(_prodFactory, "student66prod", ProdEmail);
        var prodTokens = _prodFactory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var prodClient = HostClients.Create(_prodFactory);

        // when: POST recovery/request на существующий email.
        using var prodResponse = await B11RecoveryApi.RecoveryRequestAsync(prodClient, ProdEmail);
        await B11RecoveryApi.AssertEmptyBodyOkAsync(prodResponse);

        // then: ни одна запись журнала вообще не содержит код: ни одна запись не
        // содержит адресата, категория 'EmailDev' отсутствует, ни один 6-цифровой
        // кандидат ни в одной записи не верифицируется как код хранилища.
        var prodLive = prodTokens.FindLiveForUser(prodUser.Id);
        Assert.NotNull(prodLive);
        Assert.Empty(B11RecoveryLogs.DevEmailEntries(_prodFactory.LogSink));
        foreach (var entry in _prodFactory.LogSink.Snapshot())
        {
            Assert.DoesNotContain(ProdEmail, entry.Message, StringComparison.Ordinal);
        }

        foreach (var candidate in B11RecoveryLogs.AllSixDigitCandidates(_prodFactory.LogSink))
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
