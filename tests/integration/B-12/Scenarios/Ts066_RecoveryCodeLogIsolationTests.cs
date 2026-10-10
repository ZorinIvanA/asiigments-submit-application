using LabsApp.Auth;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Observability;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B12.Scenarios;

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
    B12RecoveryDevSpyHost devFactory,
    B12RecoveryProdSecretsHost prodFactory)
    : IClassFixture<B12RecoveryDevSpyHost>, IClassFixture<B12RecoveryProdSecretsHost>
{
    private const string DevEmail = "student66dev@example.com";
    private const string ProdEmail = "student66prod@example.com";

    private readonly B12RecoveryDevSpyHost _devFactory = devFactory;
    private readonly B12RecoveryProdSecretsHost _prodFactory = prodFactory;

    [Fact]
    public async Task TS066_RecoveryRequest_CodeIsolatedToEmailDevInDevelopmentAndAbsentInProduction()
    {
        // ---------- Development ----------
        // given: Development-хост с log-sink; существующий email (DI-сид).
        var devUser = B12RecoveryStore.AddStudent(_devFactory, "student66dev", DevEmail);
        var devTokens = _devFactory.Services.GetRequiredService<ITokenService>();
        var devSecurity = _devFactory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var devClient = B12RecoveryHttp.CreateClient(_devFactory);

        // when: POST recovery/request на существующий email.
        using var devResponse = await B12RecoveryHarness.RequestRecoveryCodeAsync(devClient, DevEmail);
        await B12RecoveryHttp.AssertEmptyBodyOkAsync(devResponse);

        // then: код из записи 'EmailDev' (опознан верификацией по хэшу живого кода)
        // отсутствует в КАЖДОЙ записи вне категории 'EmailDev'.
        var devLive = devSecurity.FindLiveForUser(devUser.Id);
        Assert.NotNull(devLive);
        var devEntries = B12RecoveryLogs.DevEmailEntries(_devFactory.LogSink);
        var devEmailEntry = Assert.Single(devEntries);
        B12RecoveryLogs.AssertDevEmailFormat(devEmailEntry);
        var devCode = B12RecoveryLogs.VerifiedCode(devEmailEntry, devTokens, devLive.CodeHash);
        B12RecoveryLogs.AssertNoEntryContains(_devFactory.LogSink, devCode, B12RecoveryLogs.EmailDevCategory);

        // ---------- Production ----------
        // given: Production-хост (Auth__JwtKey задан, Seed__TeacherPassword
        // нестандартный — guard пройден); log-sink; существующий email (DI-сид).
        var prodUser = B12RecoveryStore.AddStudent(_prodFactory, "student66prod", ProdEmail);
        var prodSecurity = _prodFactory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var prodClient = B12RecoveryHttp.CreateClient(_prodFactory);

        // when: POST recovery/request на существующий email.
        using var prodResponse = await B12RecoveryHarness.RequestRecoveryCodeAsync(prodClient, ProdEmail);
        await B12RecoveryHttp.AssertEmptyBodyOkAsync(prodResponse);

        // then: ни одна запись журнала вообще не содержит код: ни одна запись не
        // содержит адресата, категория 'EmailDev' отсутствует, ни один 6-цифровой
        // кандидат ни в одной записи не верифицируется как код хранилища.
        var prodLive = prodSecurity.FindLiveForUser(prodUser.Id);
        Assert.NotNull(prodLive);
        Assert.Empty(B12RecoveryLogs.DevEmailEntries(_prodFactory.LogSink));
        foreach (var record in _prodFactory.LogSink.Snapshot())
        {
            Assert.DoesNotContain(ProdEmail, record.Message, StringComparison.Ordinal);
        }

        foreach (var candidate in B12RecoveryLogs.AllSixDigitCandidates(_prodFactory.LogSink))
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
            .Where(record => record.Level == LogLevel.Warning
                && record.Category == ProductionEmailSender.LogCategory)
            .ToArray();
        var noopWarning = Assert.Single(deliveryWarnings);
        Assert.DoesNotContain("@", noopWarning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("body=", noopWarning.Message, StringComparison.Ordinal);
    }
}
