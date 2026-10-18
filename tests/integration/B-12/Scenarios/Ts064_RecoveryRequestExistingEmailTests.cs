using LabsApp.Auth;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-064 (P0, happy_path; FR-012/NFR-006) «recovery/request: существующий email —
/// пустое тело, один живой код, код только в 'EmailDev'» — ОСНОВНОЙ прогон.
/// given: student01@example.com зарегистрирован; кодов нет; Development; тестовый
///        log-sink с категориями; счётчик KDF снимком; шов хранилища RecoveryCode
///        доступен; IEmailSender — спай-декоратор реальной dev-заглушки.
/// when:  POST /auth/recovery/request {email:'student01@example.com'}.
/// then:  200; Content-Length: 0 (тело 0 байт, НЕ '{}'); создан ровно один живой
///        RecoveryCode (expiresAt=now+10мин, attempts=0); IEmailSender вызван один
///        раз; в log-sink запись с кодом (ровно 6 ASCII-цифр, ведущие нули
///        допустимы) присутствует ТОЛЬКО в категории 'EmailDev' с маркером
///        [DEV-EMAIL]; Δkdf=0. FR-012 AC «Существующий email».
/// </summary>
public sealed class Ts064_RecoveryRequestExistingEmailTests(B12RecoveryDevSpyHost factory)
    : IClassFixture<B12RecoveryDevSpyHost>
{
    private const string StudentEmail = "student01@example.com";

    private readonly B12RecoveryDevSpyHost _factory = factory;

    [Fact]
    public async Task TS064_Request_ForExistingEmail_ReturnsEmptyBodyOneLiveCodeAndCodeOnlyInEmailDev()
    {
        // given: учётка student01@example.com (DI-сид, пароль не участвует в
        // recovery/request), кодов нет (свежий хост), KDF-счётчик снимком.
        var user = B12RecoveryStore.AddStudent(_factory, "student01", StudentEmail);
        var services = _factory.Services;
        var tokens = services.GetRequiredService<ITokenService>();
        var securityTokens = services.GetRequiredService<ISecurityTokenRepository>();
        Assert.Null(securityTokens.FindLiveForUser(user.Id));
        var kdfBefore = B12RecoveryHarness.KdfSnapshot(_factory);
        using var client = B12RecoveryHttp.CreateClient(_factory);

        // when: POST /auth/recovery/request {email:'student01@example.com'}.
        var requestedAt = _factory.Time.GetUtcNow();
        using var response = await B12RecoveryHarness.RequestRecoveryCodeAsync(client, StudentEmail);

        // then: 200; Content-Length: 0 (тело 0 байт, НЕ JSON-объект '{}') — ISS-014.
        await B12RecoveryHttp.AssertEmptyBodyOkAsync(response);

        // then: создан РОВНО ОДИН живой RecoveryCode: expiresAt=now+10мин, attempts=0.
        var live = securityTokens.FindLiveForUser(user.Id);
        Assert.NotNull(live);
        Assert.Equal(0, live.Attempts);
        Assert.Null(live.UsedAt);
        Assert.Equal(requestedAt.AddMinutes(10).UtcDateTime, live.ExpiresAt);
        var allCodes = B12RecoveryStore.AllRecoveryCodes(_factory);
        Assert.Single(allCodes);

        // then: IEmailSender вызван один раз.
        Assert.Equal(1, _factory.EmailSpy.Calls);

        // then: в log-sink запись с кодом присутствует ТОЛЬКО в категории 'EmailDev'
        // с маркером [DEV-EMAIL]; код — ровно 6 ASCII-цифр (ведущие нули допустимы),
        // опознан верификацией по хэшу хранилища.
        var devEmailEntries = B12RecoveryLogs.DevEmailEntries(_factory.LogSink);
        var devEmail = Assert.Single(devEmailEntries);
        var format = B12RecoveryLogs.AssertDevEmailFormat(devEmail);
        Assert.Equal(StudentEmail, format.Groups["to"].Value);
        var code = B12RecoveryLogs.VerifiedCode(devEmail, tokens, live.CodeHash);
        Assert.Matches("^[0-9]{6}$", code);
        B12RecoveryLogs.AssertNoEntryContains(_factory.LogSink, code, B12RecoveryLogs.EmailDevCategory);

        // then: Δkdf=0 (FR-004/ASM-005: recovery/request — 0 операций KDF).
        var kdfAfter = B12RecoveryHarness.KdfSnapshot(_factory);
        Assert.Equal(0L, B12RecoveryKdf.TotalDelta(kdfBefore, kdfAfter));
    }
}

/// <summary>
/// TS-064 (отдельный прогон) «IEmailSender подменён реализацией, бросающей
/// исключение»: recovery/request всё равно 200 с пустым телом (IF-005: ошибки
/// отправки не меняют HTTP-ответ), ошибка зафиксирована в логе без секретов
/// (NFR-006: код восстановления не попадает ни в одну запись вне 'EmailDev' —
/// здесь заглушка падает до записи письма, поэтому код не появляется нигде).
/// </summary>
public sealed class Ts064_RecoveryRequestFailingEmailSenderTests(B12RecoveryDevThrowingEmailHost factory)
    : IClassFixture<B12RecoveryDevThrowingEmailHost>
{
    private const string StudentEmail = "student01@example.com";

    private readonly B12RecoveryDevThrowingEmailHost _factory = factory;

    [Fact]
    public async Task TS064_Request_WithFailingEmailSender_StillReturnsEmptyBodyAndLogsErrorWithoutSecrets()
    {
        // given: отдельный хост с падающей заглушкой IEmailSender; учётка зарегистрирована.
        var user = B12RecoveryStore.AddStudent(_factory, "student01", StudentEmail);
        var services = _factory.Services;
        var tokens = services.GetRequiredService<ITokenService>();
        var securityTokens = services.GetRequiredService<ISecurityTokenRepository>();
        var logBefore = _factory.LogSink.Snapshot();
        using var client = B12RecoveryHttp.CreateClient(_factory);

        // when: тот же запрос recovery/request с падающим IEmailSender.
        using var response = await B12RecoveryHarness.RequestRecoveryCodeAsync(client, StudentEmail);

        // then: всё равно 200 с пустым телом (Content-Length: 0, не '{}').
        await B12RecoveryHttp.AssertEmptyBodyOkAsync(response);

        // then: заглушка вызвана ровно один раз; код известен тесту из захваченных
        // аргументов; код создан в хранилище (верифицируется по хэшу).
        Assert.Equal(1, _factory.EmailSpy.Calls);
        var captured = Assert.Single(_factory.EmailSpy.Captured);
        Assert.Equal(StudentEmail, captured[0]);
        var live = securityTokens.FindLiveForUser(user.Id);
        Assert.NotNull(live);
        var code = B12RecoveryLogs.VerifiedCode(
            new B12LogRecord(B12RecoveryLogs.EmailDevCategory, LogLevel.Information, captured[2]),
            tokens,
            live.CodeHash);
        Assert.Matches("^[0-9]{6}$", code);

        // then: ошибка зафиксирована в логе (новая запись уровня Error)...
        var newEntries = _factory.LogSink.Snapshot().Skip(logBefore.Count).ToArray();
        Assert.Contains(
            newEntries,
            entry => entry.Level == LogLevel.Error);

        // ...и БЕЗ секретов: ни одна запись журнала не содержит код восстановления.
        B12RecoveryLogs.AssertNoEntryContains(_factory.LogSink, code);
    }
}
