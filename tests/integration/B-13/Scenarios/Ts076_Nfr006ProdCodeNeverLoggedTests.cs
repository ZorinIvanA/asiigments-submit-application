using LabsApp.Auth;
using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Observability;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-076 (P0, nfr; NFR-006/FR-012) «NFR-006 (Production): код восстановления
/// не логируется вовсе».
/// given: конфигурация Production (заданы Auth__JwtKey и нестандартный
///        Seed__TeacherPassword); кастомный log-sink.
/// when:  POST recovery/request на существующий email; сканирование всех записей
///        sink на предмет 6-значного кода.
/// then:  ни одна запись не содержит код; отправка — no-op с одним warning-
///        сообщением без адресата и содержимого письма (NFR-006 verification (б);
///        глоссарий IEmailSender).
/// </summary>
public sealed class Ts076_Nfr006ProdCodeNeverLoggedTests(B13RecoveryProdSecretsHost factory)
    : IClassFixture<B13RecoveryProdSecretsHost>
{
    private const string StudentEmail = "student76prod@example.com";

    private readonly B13RecoveryProdSecretsHost _factory = factory;

    [Fact]
    public async Task TS076_RecoveryRequest_InProduction_CodeIsAbsentFromEveryLogRecord()
    {
        // given: Production-хост (guard пройден: JwtKey ≥32 симв., сид-пароль
        // нестандартный); существующий email (DI-сид); реальная композиция
        // IEmailSender НЕ подменяется — ProductionEmailSender.
        var user = B13RecoverySeed.AddStudent(_factory, "student76prod", StudentEmail);
        var services = _factory.Services;
        var tokens = services.GetRequiredService<ITokenService>();
        var securityTokens = services.GetRequiredService<ISecurityTokenRepository>();
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST recovery/request на существующий email.
        using var response = await B13RecoveryApi.RecoveryRequestAsync(client, StudentEmail);

        // then: запрос отработан (200 с пустым телом — контракт IF-008 един).
        await B13RecoveryApi.AssertEmptyBodyOkAsync(response);
        var live = securityTokens.FindLiveForUser(user.Id);
        Assert.NotNull(live);

        // then: ни одна запись журнала не содержит код: категория 'EmailDev'
        // отсутствует, адресат не упоминается, ни один 6-цифровой кандидат ни в
        // одной записи не верифицируется как код восстановления хранилища.
        Assert.Empty(B13RecoveryLogAsserts.DevEmailEntries(_factory.LogSink));
        foreach (var record in _factory.LogSink.Snapshot())
        {
            Assert.DoesNotContain(StudentEmail, record.Message, StringComparison.Ordinal);
        }

        foreach (var candidate in B13RecoveryLogAsserts.AllSixDigitCandidates(_factory.LogSink))
        {
            Assert.False(
                tokens.VerifyRecoveryCode(candidate, live.CodeHash),
                $"Кандидат «{candidate}» из записи журнала верифицировался как код восстановления " +
                "в Production — NFR-006 нарушен.");
        }

        // then: отправка — no-op с ОДНИМ warning-сообщением без адресата и
        // содержимого письма (категория ProductionEmailSender, IF-005).
        var deliveryWarnings = _factory.LogSink.Snapshot()
            .Where(record => record.Level == LogLevel.Warning
                && record.Category == ProductionEmailSender.LogCategory)
            .ToArray();
        var noopWarning = Assert.Single(deliveryWarnings);
        Assert.DoesNotContain("@", noopWarning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("body=", noopWarning.Message, StringComparison.Ordinal);
    }
}
